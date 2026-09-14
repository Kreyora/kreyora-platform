using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Storefront;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kreyora.IntegrationTests.Storefront;

public sealed class PublicStorefrontEndpointTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public PublicStorefrontEndpointTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task PublicStorefront_ResolvesByRoute_AndCompletesTheCodCheckoutFlowWithoutInternalIdentifiers()
    {
        var seededStore = await SeedPublicStoreAsync();
        var slug = seededStore.Slug;
        var variantId = seededStore.VariantId;
        await using var factory = new PublicStorefrontFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var productionRequest = new HttpRequestMessage(HttpMethod.Get, "/public/v1/store");
        productionRequest.Headers.Host = $"{slug}.kreyora.test";
        var production = await client.SendAsync(productionRequest);
        Assert.Equal(HttpStatusCode.OK, production.StatusCode);

        foreach (var invalidHost in new[] { "kreyora.test", $"extra.{slug}.kreyora.test", "127.0.0.1" })
        {
            var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/public/v1/store");
            invalidRequest.Headers.Host = invalidHost;
            var invalid = await client.SendAsync(invalidRequest);
            Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        }

        var forwardedHostRequest = new HttpRequestMessage(HttpMethod.Get, "/public/v1/store");
        forwardedHostRequest.Headers.Host = "kreyora.test";
        forwardedHostRequest.Headers.Add("X-Forwarded-Host", $"{slug}.kreyora.test");
        var forwardedHost = await client.SendAsync(forwardedHostRequest);
        Assert.Equal(HttpStatusCode.NotFound, forwardedHost.StatusCode);

        var profile = await client.GetAsync($"/public/v1/dev/stores/{slug}");

        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal("public, max-age=60", profile.Headers.CacheControl?.ToString());
        var etag = profile.Headers.ETag?.Tag;
        Assert.False(string.IsNullOrWhiteSpace(etag));
        var profileJson = await profile.Content.ReadAsStringAsync();
        Assert.Contains("Public Checkout Store", profileJson, StringComparison.Ordinal);
        Assert.DoesNotContain("tenantId", profileJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storeId", profileJson, StringComparison.OrdinalIgnoreCase);

        var cachedRequest = new HttpRequestMessage(HttpMethod.Get, $"/public/v1/dev/stores/{slug}");
        cachedRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var cached = await client.SendAsync(cachedRequest);
        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);

        var forgedRequest = new HttpRequestMessage(HttpMethod.Get, $"/public/v1/dev/stores/{slug}");
        forgedRequest.Headers.Add("X-Tenant-Id", "01J00000000000000000000999");
        var forged = await client.SendAsync(forgedRequest);
        Assert.Equal(HttpStatusCode.OK, forged.StatusCode);
        Assert.Contains("Public Checkout Store", await forged.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var catalog = await client.GetStringAsync($"/public/v1/dev/stores/{slug}/products");
        using var catalogDocument = JsonDocument.Parse(catalog);
        Assert.Equal("Public Tee", catalogDocument.RootElement.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal(variantId, catalogDocument.RootElement.GetProperty("items")[0].GetProperty("variants")[0].GetProperty("id").GetString());

        var quoteResponse = await client.PostAsJsonAsync($"/public/v1/dev/stores/{slug}/checkout/quotes", new
        {
            lines = new[] { new { variantId, quantity = 2 } },
            destination = new { countryCode = "NP", district = "Kathmandu", municipality = "KMC", locality = "Thamel" }
        });
        Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode);
        var quote = await ReadJsonAsync(quoteResponse);
        var quoteToken = quote.RootElement.GetProperty("quoteToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(quoteToken));
        Assert.False(quote.RootElement.GetProperty("delivery").TryGetProperty("ruleId", out _));

        var oversizedQuote = new HttpRequestMessage(HttpMethod.Post, $"/public/v1/dev/stores/{slug}/checkout/quotes")
        {
            Content = new StringContent(new string('x', 16 * 1024 + 1), System.Text.Encoding.UTF8, "application/json")
        };
        var oversized = await client.SendAsync(oversizedQuote);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal("no-store", oversized.Headers.CacheControl?.ToString());

        var sessionRequest = new HttpRequestMessage(HttpMethod.Post, $"/public/v1/dev/stores/{slug}/checkout/sessions")
        {
            Content = JsonContent.Create(new
            {
                quoteToken,
                customer = new { displayName = "Public Buyer", phone = "9812345678", email = "buyer@example.com", saveContact = false, privacyAcknowledged = true },
                address = new { addressLine1 = "Thamel Marg", addressLine2 = (string?)null, district = "Kathmandu", municipality = "KMC", locality = "Thamel", landmark = (string?)null }
            })
        };
        sessionRequest.Headers.Add("Idempotency-Key", "public-checkout-session");
        var sessionResponse = await client.SendAsync(sessionRequest);
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        var session = await ReadJsonAsync(sessionResponse);
        var sessionId = session.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));
        Assert.False(session.RootElement.GetProperty("items")[0].TryGetProperty("inventoryReservationId", out _));

        var orderRequest = new HttpRequestMessage(HttpMethod.Post, $"/public/v1/dev/stores/{slug}/checkout/orders")
        {
            Content = JsonContent.Create(new { checkoutSessionId = sessionId })
        };
        orderRequest.Headers.Add("Idempotency-Key", "public-checkout-order");
        var orderResponse = await client.SendAsync(orderRequest);
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await ReadJsonAsync(orderResponse);
        Assert.Equal("cashOnDelivery", order.RootElement.GetProperty("paymentMethod").GetString());
        Assert.False(order.RootElement.TryGetProperty("checkoutSessionId", out _));

        var unavailable = await client.GetAsync("/public/v1/dev/stores/not-a-real-store");
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        Assert.Contains("The storefront is unavailable.", await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicStorefront_ReadRateLimit_IsPartitionedAndReturnsRetryAfter()
    {
        var slug = (await SeedPublicStoreAsync()).Slug;
        await using var factory = new PublicStorefrontFactory(fixture.ConnectionString, readRequestsPerMinute: 1);
        using var client = factory.CreateClient();

        var first = await client.GetAsync($"/public/v1/dev/stores/{slug}");
        var limited = await client.GetAsync($"/public/v1/dev/stores/{slug}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task PublicCheckout_IgnoresTamperedCommerceFacts_AndPreservesImmutableSnapshots()
    {
        var first = await SeedPublicStoreAsync();
        var second = await SeedPublicStoreAsync();
        await using var factory = new PublicStorefrontFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var foreignQuote = await client.PostAsJsonAsync($"/public/v1/dev/stores/{first.Slug}/checkout/quotes", QuoteBody(second.VariantId, 1));
        Assert.Equal(HttpStatusCode.BadRequest, foreignQuote.StatusCode);
        Assert.DoesNotContain(second.VariantId, await foreignQuote.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var quoteResponse = await client.PostAsJsonAsync($"/public/v1/dev/stores/{first.Slug}/checkout/quotes", QuoteBody(first.VariantId, 2));
        Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode);
        using var quote = await ReadJsonAsync(quoteResponse);
        Assert.Equal(2_150m, quote.RootElement.GetProperty("totals").GetProperty("totalNpr").GetDecimal());
        Assert.False(quote.RootElement.TryGetProperty("tenantId", out _));
        var quoteToken = quote.RootElement.GetProperty("quoteToken").GetString();

        var sessionResponse = await client.SendAsync(CreateSessionRequest(first.Slug, quoteToken!, "tamper-session", saveContact: false));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        using var session = await ReadJsonAsync(sessionResponse);
        var sessionId = session.RootElement.GetProperty("id").GetString();
        Assert.Equal(2_150m, session.RootElement.GetProperty("totals").GetProperty("totalNpr").GetDecimal());

        var replayResponse = await client.SendAsync(CreateSessionRequest(first.Slug, quoteToken!, "tamper-session", saveContact: false));
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        using var replay = await ReadJsonAsync(replayResponse);
        Assert.True(replay.RootElement.GetProperty("wasReplayed").GetBoolean());

        var changedReplay = await client.SendAsync(CreateSessionRequest(first.Slug, quoteToken!, "tamper-session", saveContact: true));
        Assert.Equal(HttpStatusCode.Conflict, changedReplay.StatusCode);

        var orderResponse = await client.SendAsync(CreateOrderRequest(first.Slug, sessionId!, "tamper-order"));
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        using var order = await ReadJsonAsync(orderResponse);
        Assert.Equal("cashOnDelivery", order.RootElement.GetProperty("paymentMethod").GetString());
        Assert.Equal(2_150m, order.RootElement.GetProperty("totalNpr").GetDecimal());
        Assert.False(order.RootElement.TryGetProperty("checkoutSessionId", out _));

        var differentOrderKey = await client.SendAsync(CreateOrderRequest(first.Slug, sessionId!, "tamper-order-different"));
        Assert.Equal(HttpStatusCode.Conflict, differentOrderKey.StatusCode);

        var tenantContext = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(tenantContext);
        using var scope = tenantContext.BeginScope(OwnerContext(first.TenantId));
        var product = await db.Products.Include(item => item.Variants).SingleAsync(item => item.Id == first.ProductId);
        var deliveryRule = await db.DeliveryRules.Include(item => item.Zones).SingleAsync(item => item.Id == first.DeliveryRuleId);
        product.UpdateDetails("Changed title", "Changed description", "changed-title");
        product.UpdateVariant(first.VariantId, "PUBLIC-TEE-CHANGED", "Changed variant", null, 1_900m, null, true);
        product.Unpublish();
        deliveryRule.Update(new DeliveryRuleSettings("Changed delivery", 0, DeliveryFeeType.Flat, 999m, null, "Tomorrow", false, true,
            [new DeliveryZoneInput("Kathmandu", "KMC", "Thamel")]));
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var immutable = await db.Orders.Include(item => item.Items).SingleAsync();
        Assert.Equal("Public Tee", immutable.Items.Single().ProductTitle);
        Assert.Equal("Standard", immutable.Items.Single().VariantName);
        Assert.Equal(1_000m, immutable.Items.Single().UnitPriceNpr);
        Assert.Equal("Kathmandu COD", immutable.DeliveryRuleName);
        Assert.Equal(150m, immutable.DeliveryFeeNpr);
        Assert.Equal(2_150m, immutable.TotalNpr);
        Assert.Single(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task PublicStorefront_SeparatesStorefrontsAcrossEtagHostSlugAndSessionSelectors()
    {
        var first = await SeedPublicStoreAsync();
        var second = await SeedPublicStoreAsync();
        await using var factory = new PublicStorefrontFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var firstResponse = await client.GetAsync($"/public/v1/dev/stores/{first.Slug}");
        var etag = firstResponse.Headers.ETag?.Tag;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(etag));

        var secondRequest = new HttpRequestMessage(HttpMethod.Get, $"/public/v1/dev/stores/{second.Slug}");
        secondRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var secondResponse = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Contains("Public Checkout Store", await secondResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("Host", secondResponse.Headers.Vary.Single());

        var missingRoute = await client.GetAsync("/public/v1/dev/stores/not-a-real-store");
        var invalidHostRequest = new HttpRequestMessage(HttpMethod.Get, "/public/v1/store");
        invalidHostRequest.Headers.Host = "extra.invalid.kreyora.test";
        var invalidHost = await client.SendAsync(invalidHostRequest);
        Assert.Equal(HttpStatusCode.NotFound, missingRoute.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalidHost.StatusCode);
        Assert.Contains("The storefront is unavailable.", await missingRoute.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("The storefront is unavailable.", await invalidHost.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var quoteToken = await CreateQuoteTokenAsync(client, first.Slug, first.VariantId, 1);
        var sessionResponse = await client.SendAsync(CreateSessionRequest(first.Slug, quoteToken, "isolation-session", saveContact: false));
        using var session = await ReadJsonAsync(sessionResponse);
        var foreignOrder = await client.SendAsync(CreateOrderRequest(second.Slug, session.RootElement.GetProperty("id").GetString()!, "foreign-order"));
        Assert.Equal(HttpStatusCode.NotFound, foreignOrder.StatusCode);
        Assert.DoesNotContain(first.TenantId, await foreignOrder.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicCheckout_ReservesTheLastUnitOnce_AndCreatesOnlyOneOrderUnderContention()
    {
        var store = await SeedPublicStoreAsync(stock: 1);
        await using var factory = new PublicStorefrontFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var firstQuote = await CreateQuoteTokenAsync(client, store.Slug, store.VariantId, 1);
        var secondQuote = await CreateQuoteTokenAsync(client, store.Slug, store.VariantId, 1);
        var sessionResponses = await Task.WhenAll(
            client.SendAsync(CreateSessionRequest(store.Slug, firstQuote, "last-unit-session-one", saveContact: false)),
            client.SendAsync(CreateSessionRequest(store.Slug, secondQuote, "last-unit-session-two", saveContact: false)));

        Assert.Single(sessionResponses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(sessionResponses, response => response.StatusCode == HttpStatusCode.BadRequest);
        var winner = sessionResponses.Single(response => response.StatusCode == HttpStatusCode.Created);
        using var winningSession = await ReadJsonAsync(winner);
        var sessionId = winningSession.RootElement.GetProperty("id").GetString()!;

        var orderResponses = await Task.WhenAll(
            client.SendAsync(CreateOrderRequest(store.Slug, sessionId, "last-unit-order-one")),
            client.SendAsync(CreateOrderRequest(store.Slug, sessionId, "last-unit-order-two")));
        Assert.Single(orderResponses, response => response.StatusCode == HttpStatusCode.Created);
        var losingOrder = orderResponses.Single(response => response.StatusCode != HttpStatusCode.Created);
        Assert.True(losingOrder.StatusCode == HttpStatusCode.Conflict, await losingOrder.Content.ReadAsStringAsync());

        var tenantContext = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(tenantContext);
        using var scope = tenantContext.BeginScope(OwnerContext(store.TenantId));
        var inventory = await db.InventoryItems.SingleAsync(item => item.VariantId == store.VariantId);
        Assert.Single(await db.Orders.ToListAsync());
        Assert.Single(await db.StockMovements.Where(item => item.Type == StockMovementType.ReservationCommitted).ToListAsync());
        Assert.Equal(0, inventory.OnHandQuantity);
        Assert.Equal(0, inventory.ReservedQuantity);
        Assert.Empty(await db.InventoryReservations.Where(item => item.State == InventoryReservationState.Active).ToListAsync());
    }

    [Fact]
    public async Task CheckoutSessionExpiryJob_IsRetrySafe_AndWinsAgainstAnExpiredOrder()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var store = await SeedPublicStoreAsync(stock: 3);
        await using var factory = new PublicStorefrontFactory(fixture.ConnectionString, timeProvider: clock);
        using var client = factory.CreateClient();

        var quoteToken = await CreateQuoteTokenAsync(client, store.Slug, store.VariantId, 2);
        var sessionResponse = await client.SendAsync(CreateSessionRequest(store.Slug, quoteToken, "expiry-session", saveContact: false));
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        using var session = await ReadJsonAsync(sessionResponse);
        var sessionId = session.RootElement.GetProperty("id").GetString()!;
        clock.UtcNow = clock.UtcNow.AddMinutes(11);

        using var serviceScope = factory.Services.CreateScope();
        var expiryJob = serviceScope.ServiceProvider.GetRequiredService<CheckoutSessionExpiryJob>();
        var expiredOrder = client.SendAsync(CreateOrderRequest(store.Slug, sessionId, "expiry-order"));
        var expiryRun = expiryJob.RunAsync();
        await Task.WhenAll(expiredOrder, expiryRun);
        var expiredOrderResponse = await expiredOrder;
        Assert.Equal(HttpStatusCode.Conflict, expiredOrderResponse.StatusCode);
        await expiryJob.RunAsync();

        var tenantContext = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(tenantContext);
        using var scope = tenantContext.BeginScope(OwnerContext(store.TenantId));
        var checkoutSession = await db.CheckoutSessions.SingleAsync();
        var reservation = await db.InventoryReservations.SingleAsync();
        var inventory = await db.InventoryItems.SingleAsync(item => item.VariantId == store.VariantId);
        Assert.Equal(CheckoutSessionState.Expired, checkoutSession.State);
        Assert.Equal(InventoryReservationState.Expired, reservation.State);
        Assert.Equal(3, inventory.OnHandQuantity);
        Assert.Equal(0, inventory.ReservedQuantity);
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Single(await db.AuditEvents.Where(item => item.Action == "checkout-session.expired").ToListAsync());
    }

    private async Task<SeededPublicStore> SeedPublicStoreAsync(int stock = 5)
    {
        var tenantContext = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(tenantContext);
        await db.Database.MigrateAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = Tenant.Create("Public Checkout Tenant", $"public-{suffix}");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        using var scope = tenantContext.BeginScope(new TenantContext(tenant.Id, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner));
        var slug = $"public-{suffix}";
        var store = Store.Create(tenant.Id, new StoreSettings("Public Checkout Store", slug, "A ready public store", StoreThemePreset.Default, null,
            "Public Seller", "seller@example.com", "+9779812345678", null, null, null, null, "Terms", "Privacy", "Returns", "COD accepted"));
        store.Activate(DateTimeOffset.UtcNow);
        var product = Product.Create(tenant.Id, "Public Tee", "A public product", $"public-tee-{suffix}");
        var variant = product.AddVariant("PUBLIC-TEE", "Standard", null, 1_000m, null, true);
        product.Publish();
        var inventory = InventoryItem.Create(tenant.Id, variant.Id);
        inventory.ApplyMovement(stock);
        var deliveryRule = DeliveryRule.Create(tenant.Id, store.Id, new DeliveryRuleSettings("Kathmandu COD", 0, DeliveryFeeType.Flat, 150m, null, "1-2 business days", true, true,
            [new DeliveryZoneInput("Kathmandu", "KMC", "Thamel")]));
        db.AddRange(store, product, inventory,
            StoreProductPublication.Create(tenant.Id, store.Id, product.Id, StoreProductVisibility.Visible),
            deliveryRule);
        await db.SaveChangesAsync();
        return new SeededPublicStore(tenant.Id, store.Id, slug, product.Id, variant.Id, deliveryRule.Id);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static object QuoteBody(string variantId, int quantity) => new
    {
        lines = new[] { new { variantId, quantity, unitPriceNpr = 1m, lineTotalNpr = 1m, publicationState = "visible" } },
        destination = new { countryCode = "NP", district = "Kathmandu", municipality = "KMC", locality = "Thamel" },
        tenantId = "forged-tenant",
        storeId = "forged-store",
        totalNpr = 1m,
        deliveryFeeNpr = 0m,
        paymentStatus = "paid"
    };

    private static async Task<string> CreateQuoteTokenAsync(HttpClient client, string slug, string variantId, int quantity)
    {
        var response = await client.PostAsJsonAsync($"/public/v1/dev/stores/{slug}/checkout/quotes", QuoteBody(variantId, quantity));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var quote = await ReadJsonAsync(response);
        return quote.RootElement.GetProperty("quoteToken").GetString()!;
    }

    private static HttpRequestMessage CreateSessionRequest(string slug, string quoteToken, string idempotencyKey, bool saveContact)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/public/v1/dev/stores/{slug}/checkout/sessions")
        {
            Content = JsonContent.Create(new
            {
                quoteToken,
                customer = new { displayName = "Public Buyer", phone = "9812345678", email = "buyer@example.com", saveContact, privacyAcknowledged = true, tenantId = "forged-tenant" },
                address = new { addressLine1 = "Thamel Marg", addressLine2 = (string?)null, district = "Kathmandu", municipality = "KMC", locality = "Thamel", landmark = (string?)null, deliveryFeeNpr = 0m },
                totalNpr = 1m,
                paymentStatus = "paid"
            })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static HttpRequestMessage CreateOrderRequest(string slug, string sessionId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/public/v1/dev/stores/{slug}/checkout/orders")
        {
            Content = JsonContent.Create(new { checkoutSessionId = sessionId, tenantId = "forged-tenant", storeId = "forged-store", totalNpr = 1m, deliveryFeeNpr = 0m, paymentStatus = "paid", paymentMethod = "merchantQr" })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static TenantContext OwnerContext(string tenantId) => new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private sealed record SeededPublicStore(string TenantId, string StoreId, string Slug, string ProductId, string VariantId, string DeliveryRuleId);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : ITimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class PublicStorefrontFactory(string connectionString, int? readRequestsPerMinute = null, ITimeProvider? timeProvider = null) : WebApplicationFactory<Kreyora.WebApi.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ITenantContextResolutionService, NoOpTenantContextResolutionService>();
                if (timeProvider is not null)
                {
                    services.RemoveAll<ITimeProvider>();
                    services.AddSingleton(timeProvider);
                }
            });
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                ["Database:ConnectionString"] = connectionString,
                ["PublicStorefront:PlatformBaseDomain"] = "kreyora.test",
                ["PublicStorefront:EnableDevelopmentSlugRoutes"] = "true",
                ["Email:Smtp:ApplicationName"] = "Kreyora Test",
                ["Email:Smtp:Host"] = "smtp.kreyora.test",
                ["Email:Smtp:Port"] = "587",
                ["Email:Smtp:Security"] = "StartTls",
                ["Email:Smtp:SenderEmail"] = "no-reply@kreyora.test",
                ["Email:Smtp:SenderDisplayName"] = "Kreyora Test",
                    ["Email:Smtp:ApplicationPublicUrl"] = "https://seller.kreyora.test"
                };
                if (readRequestsPerMinute is not null) values["PublicStorefront:ReadRequestsPerMinute"] = readRequestsPerMinute.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                configuration.AddInMemoryCollection(values);
            });
        }

        private sealed class NoOpTenantContextResolutionService(AppDbContext dbContext) : ITenantContextResolutionService
        {
            public Task<IReadOnlyList<WorkspaceSummary>> GetActiveWorkspacesAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);
            public Task<TenantContext?> ResolveMembershipContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TenantContext?>(null);
            public async Task<TenantContext?> ResolveBackgroundContextAsync(string tenantId, CancellationToken cancellationToken = default)
            {
                var tenant = await dbContext.Tenants.AsNoTracking().SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
                return tenant?.Status == TenantStatus.Active ? new TenantContext(tenant.Id, null, null, null) : null;
            }
            public Task<TenantContext?> ResolveSupportContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TenantContext?>(null);
        }
    }
}
