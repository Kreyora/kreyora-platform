using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kreyora.Application.Audit;
using Kreyora.Application.Orders;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Common;
using Kreyora.Domain.Orders;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Orders;

/// <summary>
/// Customer order status for the assistant (M09-S04 Q4). Linked orders need no proof; any other order needs its number
/// plus the last four digits of its phone. Failures answer uniformly and are counted per order: five in 24 hours lock
/// chat lookups of that order (audited). Returns status fields only, never PII or totals.
/// </summary>
public sealed partial class OrderStatusLookupService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IAuditEventService auditEvents,
    ITimeProvider timeProvider) : IOrderStatusLookupService
{
    private const int MaxLinkedOrders = 5;
    private const int MaxConcurrencyRetries = 3;

    [GeneratedRegex("^ORD-[0-9A-Z]{26}$", RegexOptions.CultureInvariant)]
    private static partial Regex OrderNumberPattern();

    [GeneratedRegex("^[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneSuffixPattern();

    public async Task<OrderStatusLookupResult> LookupAsync(OrderStatusLookupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var orderNumber = NormalizeOrderNumber(request.OrderNumber);

        // Linked orders need no proof: the chat customer's own orders, and orders placed through this chat's assistant
        // checkout links (M09-S05; works even when the customer chose not to save their contact details).
        var customerId = request.CustomerId;
        var linkedOrderIds = request.ConversationId is null ? [] : await dbContext.AssistantCheckoutLinks.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ConversationId == request.ConversationId && l.OrderId != null)
            .Select(l => l.OrderId!).ToListAsync(cancellationToken);
        if (customerId is not null || linkedOrderIds.Count > 0)
        {
            var linked = await StatusAsync(tenantId, o => ((customerId != null && o.CustomerId == customerId) || linkedOrderIds.Contains(o.Id))
                && (orderNumber == null || o.OrderNumber == orderNumber), cancellationToken);
            if (linked.Count > 0) return Found(linked);
        }

        if (string.IsNullOrWhiteSpace(request.OrderNumber) || string.IsNullOrWhiteSpace(request.PhoneLast4))
        {
            return new OrderStatusLookupResult(OrderStatusLookupOutcome.VerificationRequired, []);
        }

        if (orderNumber is null || !PhoneSuffixPattern().IsMatch(request.PhoneLast4))
        {
            return NotVerified(); // malformed input answers exactly like a wrong pair
        }

        var order = await dbContext.Orders.AsNoTracking().Where(o => o.TenantId == tenantId && o.OrderNumber == orderNumber)
            .Select(o => new { o.Id, o.CustomerPhone }).SingleOrDefaultAsync(cancellationToken);
        if (order is null) return NotVerified();

        var now = timeProvider.UtcNow;
        var digits = new string(order.CustomerPhone.Where(char.IsAsciiDigit).ToArray());
        var verified = digits.Length >= 4 && string.Equals(digits[^4..], request.PhoneLast4, StringComparison.Ordinal);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var guard = await dbContext.OrderLookupGuards.SingleOrDefaultAsync(g => g.TenantId == tenantId && g.OrderId == order.Id, cancellationToken);
                if (guard?.IsLocked(now) == true) return new OrderStatusLookupResult(OrderStatusLookupOutcome.Locked, []);

                if (verified)
                {
                    if (guard is { FailureCount: > 0 })
                    {
                        guard.Reset(now);
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }

                    return Found(await StatusAsync(tenantId, o => o.Id == order.Id, cancellationToken));
                }

                if (guard is null)
                {
                    guard = OrderLookupGuard.Create(tenantId, order.Id, now);
                    dbContext.OrderLookupGuards.Add(guard);
                }

                var lockedNow = guard.RegisterFailure(now);
                var metadata = JsonSerializer.Serialize(new { conversationId = request.ConversationId, failures = guard.FailureCount });
                await auditEvents.AppendAsync(new AuditEventWrite("assistant.order_lookup.failed", "order", order.Id,
                    Metadata: metadata, ActorKind: CommerceActorKind.CommerceSystem), cancellationToken);
                if (lockedNow)
                {
                    await auditEvents.AppendAsync(new AuditEventWrite("assistant.order_lookup.locked", "order", order.Id,
                        Metadata: metadata, ActorKind: CommerceActorKind.CommerceSystem), cancellationToken);
                }

                return NotVerified();
            }
            catch (DbUpdateException) when (attempt < MaxConcurrencyRetries)
            {
                // Concurrent failures for the same order (xmin or the unique index): recount from fresh state.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private Task<List<StatusRow>> StatusAsync(string tenantId, Expression<Func<Order, bool>> filter, CancellationToken cancellationToken) =>
        dbContext.Orders.AsNoTracking().Where(o => o.TenantId == tenantId).Where(filter)
            .OrderByDescending(o => o.CreatedAt).Take(MaxLinkedOrders)
            .Select(o => new StatusRow(o.Id, o.CustomerId, o.OrderNumber, o.Status, o.PaymentStatus, o.FulfilmentStatus, o.CreatedAt, o.Items.Count, o.EstimatedEtaText))
            .ToListAsync(cancellationToken);

    private static OrderStatusLookupResult Found(List<StatusRow> rows) =>
        new(OrderStatusLookupOutcome.Found, [.. rows.Select(r => new CustomerOrderStatus(r.OrderNumber, r.Status, r.PaymentStatus, r.FulfilmentStatus, r.CreatedAt, r.ItemCount, r.EtaText))]);

    private static OrderStatusLookupResult NotVerified() => new(OrderStatusLookupOutcome.NotVerified, []);

    /// <summary>Accepts "ORD-…" or the bare ULID, any case; anything else is null.</summary>
    public static string? NormalizeOrderNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var upper = value.Trim().ToUpperInvariant();
        var candidate = upper.StartsWith("ORD-", StringComparison.Ordinal) ? upper : "ORD-" + upper;
        return OrderNumberPattern().IsMatch(candidate) ? candidate : null;
    }

    private sealed record StatusRow(string Id, string? CustomerId, string OrderNumber, OrderStatus Status, PaymentStatus PaymentStatus, FulfilmentStatus FulfilmentStatus, DateTimeOffset CreatedAt, int ItemCount, string? EtaText);
}
