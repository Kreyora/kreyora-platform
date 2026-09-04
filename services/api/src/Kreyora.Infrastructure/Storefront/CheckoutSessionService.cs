using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Customers;
using Kreyora.Application.Inventory;
using Kreyora.Application.Models;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Storefront;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Kreyora.Infrastructure.Storefront;

public sealed class CheckoutSessionService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IStorefrontQuoteService quotes,
    ICustomerCheckoutService customers,
    ICheckoutInventoryReservationService inventory,
    IAuditEventService auditEvents,
    Domain.Abstractions.ITimeProvider timeProvider,
    IOptions<CheckoutSessionOptions> options) : IStorefrontCheckoutSessionService
{
    private const string CreateOperation = "checkout-session.create";

    public async Task<Result<CheckoutSessionItemResult>> CreateAsync(CreateCheckoutSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = tenantContext.RequireCurrent();
        try
        {
            if (!request.Customer.PrivacyAcknowledged) return Result<CheckoutSessionItemResult>.ValidationError("Privacy acknowledgement is required to start checkout.");
            var quoteFingerprint = Fingerprint(request.QuoteToken);
            var fingerprint = Fingerprint(new { quoteFingerprint, request.Customer.SaveContact, request.Customer.PrivacyAcknowledged });
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var replay = await FindReplayAsync(request.IdempotencyKey, fingerprint, cancellationToken);
            if (replay is not null) return replay;

            var quote = await quotes.RevalidateForCheckoutAsync(request.QuoteToken, cancellationToken);
            if (quote.IsFailure) return Result<CheckoutSessionItemResult>.Failure(quote.Error!);
            var revalidatedQuote = quote.Value!;
            var store = await dbContext.Stores.SingleOrDefaultAsync(store => store.Id == revalidatedQuote.StoreId, cancellationToken);
            if (store is null) return Result<CheckoutSessionItemResult>.NotFound("The store is unavailable.");
            EnsureAddressMatchesQuote(request.Address, revalidatedQuote.Destination);
            var now = timeProvider.UtcNow;
            var expiresAt = Min(revalidatedQuote.QuoteExpiresAt, now.AddMinutes(options.Value.LifetimeMinutes));
            var privacyFingerprint = Fingerprint(store.PrivacyPolicy ?? string.Empty);
            var profile = await customers.ResolveAsync(new CustomerCheckoutContact(request.Customer.DisplayName, request.Customer.Phone, request.Customer.Email, request.Customer.SaveContact),
                privacyFingerprint, now, now.AddDays(options.Value.PiiReviewDays), cancellationToken);
            if (profile.IsFailure) return Result<CheckoutSessionItemResult>.Failure(profile.Error!);

            var session = CheckoutSession.Create(new CheckoutSessionCreation(
                context.TenantId, store.Id, profile.Value!.CustomerId, quoteFingerprint, revalidatedQuote.QuoteExpiresAt, expiresAt,
                profile.Value.DisplayName, profile.Value.Phone, profile.Value.Email, request.Address.AddressLine1, request.Address.AddressLine2,
                request.Address.District, request.Address.Municipality, request.Address.Locality, request.Address.Landmark, privacyFingerprint,
                now.AddDays(options.Value.PiiReviewDays), revalidatedQuote.Totals.MerchandiseSubtotalNpr, revalidatedQuote.Totals.DiscountNpr, revalidatedQuote.Totals.DeliveryFeeNpr,
                revalidatedQuote.Totals.TaxNpr, revalidatedQuote.Totals.ProviderFeeNpr, revalidatedQuote.Totals.PlatformFeeNpr, revalidatedQuote.Totals.TotalNpr, revalidatedQuote.Totals.Currency,
                revalidatedQuote.Delivery.RuleId, revalidatedQuote.Delivery.RuleName, revalidatedQuote.Delivery.EstimatedEtaText, revalidatedQuote.Delivery.CodAvailable, now));
            dbContext.CheckoutSessions.Add(session);
            var reservations = await inventory.ReserveForCheckoutAsync(new CheckoutInventoryReservationRequest(session.Id,
                revalidatedQuote.Lines.Select(line => new CheckoutInventoryLine(line.VariantId, line.Quantity)).ToArray(), expiresAt), cancellationToken);
            if (reservations.IsFailure)
            {
                var error = reservations.Error!;
                dbContext.ChangeTracker.Clear();
                return Result<CheckoutSessionItemResult>.Failure(error);
            }
            foreach (var line in revalidatedQuote.Lines)
            {
                var reservation = reservations.Value!.Single(item => item.VariantId == line.VariantId);
                var item = CheckoutSessionItem.Create(context.TenantId, session.Id, reservation.InventoryReservationId, line.ProductId, line.ProductTitle, line.VariantId, line.VariantName, line.Quantity, line.UnitPriceNpr);
                session.AddItem(item);
                dbContext.CheckoutSessionItems.Add(item);
            }
            dbContext.CheckoutSessionCommands.Add(CheckoutSessionCommand.Create(context.TenantId, CreateOperation, NormalizeKey(request.IdempotencyKey), fingerprint, session.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("checkout-session.created", "checkout-session", session.Id,
                Metadata: $"{{\"lineCount\":{session.Items.Count},\"reservationCount\":{reservations.Value!.Count}}}"), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<CheckoutSessionItemResult>.Success(Map(session, false));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            return Result<CheckoutSessionItemResult>.Conflict("A checkout session is already active for this quote.");
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            return Result<CheckoutSessionItemResult>.ValidationError(exception.Message);
        }
    }

    public async Task<int> ExpireDueSessionsAsync(CancellationToken cancellationToken = default)
    {
        tenantContext.RequireCurrent();
        var due = await dbContext.CheckoutSessions.Where(session => session.State == CheckoutSessionState.Active && session.ExpiresAt <= timeProvider.UtcNow)
            .OrderBy(session => session.ExpiresAt).ThenBy(session => session.Id).Take(options.Value.ExpiryBatchSize).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var candidate in due)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var session = await dbContext.CheckoutSessions.Include(item => item.Items).SingleOrDefaultAsync(item => item.Id == candidate.Id, cancellationToken);
            if (session is null || session.State != CheckoutSessionState.Active || session.ExpiresAt > timeProvider.UtcNow) continue;
            await inventory.ExpireForCheckoutAsync(session.Id, cancellationToken);
            session.Expire(timeProvider.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditEvents.AppendAsync(new AuditEventWrite("checkout-session.expired", "checkout-session", session.Id,
                Metadata: $"{{\"reservationCount\":{session.Items.Count},\"automated\":true}}"), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            count++;
        }
        return count;
    }

    private async Task<Result<CheckoutSessionItemResult>?> FindReplayAsync(string idempotencyKey, string fingerprint, CancellationToken cancellationToken)
    {
        var command = await dbContext.CheckoutSessionCommands.SingleOrDefaultAsync(command => command.Operation == CreateOperation && command.IdempotencyKey == NormalizeKey(idempotencyKey), cancellationToken);
        if (command is null) return null;
        if (!string.Equals(command.RequestFingerprint, fingerprint, StringComparison.Ordinal)) return Result<CheckoutSessionItemResult>.Conflict("The idempotency key was already used for a different checkout request.");
        var session = await dbContext.CheckoutSessions.Include(item => item.Items).SingleAsync(item => item.Id == command.CheckoutSessionId, cancellationToken);
        return Result<CheckoutSessionItemResult>.Success(Map(session, true));
    }

    private static void EnsureAddressMatchesQuote(CheckoutAddressInput address, StorefrontDestinationInput destination)
    {
        var actual = DeliveryRuleZone.NormalizeDestination(new DeliveryDestinationInput("NP", address.District, address.Municipality, address.Locality));
        var expected = DeliveryRuleZone.NormalizeDestination(new DeliveryDestinationInput(destination.CountryCode, destination.District, destination.Municipality, destination.Locality));
        if (actual.NormalizedDistrict != expected.NormalizedDistrict || actual.NormalizedMunicipality != expected.NormalizedMunicipality || actual.NormalizedLocality != expected.NormalizedLocality)
            throw new ArgumentException("The delivery address must match the quoted destination.", nameof(address));
    }

    private static CheckoutSessionItemResult Map(CheckoutSession session, bool replayed) => new(session.Id, session.StoreId, session.CustomerId, session.ExpiresAt,
        session.Items.Select(item => new CheckoutSessionLineItem(item.VariantId, item.Quantity, item.InventoryReservationId, item.UnitPriceNpr, item.LineSubtotalNpr)).ToArray(),
        new StorefrontQuoteDelivery(session.DeliveryRuleId, session.DeliveryRuleName, session.DeliveryFeeNpr, session.EstimatedEtaText, session.CodAvailable),
        new StorefrontQuoteTotals(session.MerchandiseSubtotalNpr, session.DiscountNpr, session.DeliveryFeeNpr, session.TaxNpr, session.ProviderFeeNpr, session.PlatformFeeNpr, session.TotalNpr, session.Currency), replayed);
    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first <= second ? first : second;
    private static string NormalizeKey(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("An idempotency key is required.", nameof(value)) : value.Trim().Length > 256 ? throw new ArgumentOutOfRangeException(nameof(value)) : value.Trim();
    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
