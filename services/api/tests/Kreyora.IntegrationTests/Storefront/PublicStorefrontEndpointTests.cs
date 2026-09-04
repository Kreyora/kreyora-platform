using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.IntegrationTests.Storefront;

public sealed class PublicStorefrontEndpointTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public PublicStorefrontEndpointTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task PublicStorefront_ResolvesByRoute_AndCompletesTheCodCheckoutFlowWithoutInternalIdentifiers()
    {
        var (slug, variantId) = await SeedPublicStoreAsync();
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
        var (slug, _) = await SeedPublicStoreAsync();
        await using var factory = new PublicStorefrontFactory(fixture.ConnectionString, readRequestsPerMinute: 1);
        using var client = factory.CreateClient();

        var first = await client.GetAsync($"/public/v1/dev/stores/{slug}");
        var limited = await client.GetAsync($"/public/v1/dev/stores/{slug}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
    }

    private async Task<(string Slug, string VariantId)> SeedPublicStoreAsync()
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
        inventory.ApplyMovement(5);
        db.AddRange(store, product, inventory,
            StoreProductPublication.Create(tenant.Id, store.Id, product.Id, StoreProductVisibility.Visible),
            DeliveryRule.Create(tenant.Id, store.Id, new DeliveryRuleSettings("Kathmandu COD", 0, DeliveryFeeType.Flat, 150m, null, "1-2 business days", true, true,
                [new DeliveryZoneInput("Kathmandu", "KMC", "Thamel")])));
        await db.SaveChangesAsync();
        return (slug, variant.Id);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class PublicStorefrontFactory(string connectionString, int? readRequestsPerMinute = null) : WebApplicationFactory<Kreyora.WebApi.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.ConfigureServices(services => services.AddScoped<ITenantContextResolutionService, NoOpTenantContextResolutionService>());
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

        private sealed class NoOpTenantContextResolutionService : ITenantContextResolutionService
        {
            public Task<IReadOnlyList<WorkspaceSummary>> GetActiveWorkspacesAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);
            public Task<TenantContext?> ResolveMembershipContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TenantContext?>(null);
            public Task<TenantContext?> ResolveBackgroundContextAsync(string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TenantContext?>(null);
            public Task<TenantContext?> ResolveSupportContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TenantContext?>(null);
        }
    }
}
