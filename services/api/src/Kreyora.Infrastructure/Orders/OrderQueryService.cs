using Kreyora.Application.Authorization;
using Kreyora.Application.Models;
using Kreyora.Application.Notifications;
using Kreyora.Application.Orders;
using Kreyora.Application.Payments;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Orders;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Orders;

public sealed class OrderQueryService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer authorizer) : IOrderQueryService
{
    public async Task<Result<PagedResult<OrderSummaryItem>>> ListOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.OrdersRead);
        var context = tenantContext.RequireCurrent();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var queryable = dbContext.Orders.AsNoTracking()
            .Where(o => o.TenantId == context.TenantId);

        if (query.Status.HasValue)
        {
            queryable = queryable.Where(o => o.Status == query.Status.Value);
        }

        if (query.PaymentStatus.HasValue)
        {
            queryable = queryable.Where(o => o.PaymentStatus == query.PaymentStatus.Value);
        }

        if (query.FulfilmentStatus.HasValue)
        {
            queryable = queryable.Where(o => o.FulfilmentStatus == query.FulfilmentStatus.Value);
        }

        if (query.PaymentMethod.HasValue)
        {
            queryable = queryable.Where(o => o.PaymentMethod == query.PaymentMethod.Value);
        }

        if (query.Source.HasValue)
        {
            queryable = queryable.Where(o => o.Source == query.Source.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            queryable = queryable.Where(o =>
                o.OrderNumber.Contains(search) ||
                o.CustomerName.Contains(search) ||
                o.CustomerPhone.Contains(search));
        }

        var total = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryItem(
                o.Id,
                o.OrderNumber,
                o.Status,
                o.PaymentStatus,
                o.FulfilmentStatus,
                o.PaymentMethod,
                o.Source,
                o.CustomerName,
                o.CustomerPhone,
                o.CustomerEmail,
                o.District,
                o.TotalNpr,
                o.Currency,
                o.Items.Count,
                EF.Property<uint>(o, "xmin"),
                o.CreatedAt,
                o.ModifiedAt))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<OrderSummaryItem>>.Success(new PagedResult<OrderSummaryItem>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    public async Task<Result<OrderDetailItem>> GetOrderDetailAsync(string orderId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.OrdersRead);
        var context = tenantContext.RequireCurrent();

        var normalizedId = Require(orderId, nameof(orderId), 26);
        var order = await dbContext.Orders
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == normalizedId && o.TenantId == context.TenantId, cancellationToken);

        if (order is null)
        {
            return Result<OrderDetailItem>.NotFound("Order not found.");
        }

        var rowVersion = dbContext.Entry(order).Property<uint>("xmin").CurrentValue;

        var paymentAttempts = await dbContext.PaymentAttempts.AsNoTracking()
            .Include(pa => pa.Proofs)
            .Where(pa => pa.TenantId == context.TenantId && pa.OrderId == normalizedId)
            .OrderByDescending(pa => pa.CreatedAt)
            .Select(pa => new PaymentAttemptItem(
                pa.Id,
                pa.OrderId,
                pa.Method,
                pa.Status,
                pa.AmountNpr,
                pa.Currency,
                pa.InternalReference,
                pa.ProviderReference,
                pa.VerifiedAt,
                pa.VerifiedByUserId,
                pa.RejectedAt,
                pa.RejectedByUserId,
                pa.RejectionReason,
                pa.CollectedAt,
                pa.CollectedByUserId,
                pa.Proofs.Select(p => new PaymentProofItem(
                    p.Id,
                    p.PaymentAttemptId,
                    p.ContentType,
                    p.ByteSize,
                    p.Status,
                    p.CustomerNote,
                    p.UploadExpiresAt,
                    p.ReadyAt)).ToList()))
            .ToListAsync(cancellationToken);

        var items = order.Items.Select(i => new OrderItemDetail(
            i.Id,
            i.ProductId,
            i.ProductTitle,
            i.VariantId,
            i.VariantName,
            i.UnitPriceNpr,
            i.Quantity,
            i.LineSubtotalNpr,
            "NPR")).ToList();

        var detail = new OrderDetailItem(
            order.Id,
            order.OrderNumber,
            order.StoreId,
            order.CheckoutSessionId,
            order.Status,
            order.PaymentStatus,
            order.FulfilmentStatus,
            order.PaymentMethod,
            order.Source,
            order.CustomerName,
            order.CustomerPhone,
            order.CustomerEmail,
            order.AddressLine1,
            order.AddressLine2,
            order.District,
            order.Municipality,
            order.Locality,
            order.Landmark,
            order.MerchandiseSubtotalNpr,
            order.DiscountNpr,
            order.DeliveryFeeNpr,
            order.TaxNpr,
            order.TotalNpr,
            order.Currency,
            order.DeliveryRuleId,
            order.DeliveryRuleName,
            order.EstimatedEtaText,
            order.CodAvailable,
            rowVersion,
            order.CreatedAt,
            order.ModifiedAt,
            items,
            paymentAttempts);

        return Result<OrderDetailItem>.Success(detail);
    }

    public async Task<Result<IReadOnlyList<OrderActivityItem>>> GetOrderActivityAsync(string orderId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.OrdersRead);
        var context = tenantContext.RequireCurrent();

        var normalizedId = Require(orderId, nameof(orderId), 26);
        var orderExists = await dbContext.Orders.AsNoTracking()
            .AnyAsync(o => o.Id == normalizedId && o.TenantId == context.TenantId, cancellationToken);

        if (!orderExists)
        {
            return Result<IReadOnlyList<OrderActivityItem>>.NotFound("Order not found.");
        }

        var events = await dbContext.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == context.TenantId && e.TargetType == "order" && e.TargetId == normalizedId)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new OrderActivityItem(
                e.Id,
                e.Action,
                e.ActorUserId,
                e.OccurredAt,
                e.Reason,
                e.Metadata))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<OrderActivityItem>>.Success(events);
    }

    public async Task<Result<IReadOnlyList<OrderNotificationItem>>> GetOrderNotificationsAsync(string orderId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.OrdersRead);
        var context = tenantContext.RequireCurrent();

        var normalizedId = Require(orderId, nameof(orderId), 26);
        var orderExists = await dbContext.Orders.AsNoTracking()
            .AnyAsync(o => o.Id == normalizedId && o.TenantId == context.TenantId, cancellationToken);

        if (!orderExists)
        {
            return Result<IReadOnlyList<OrderNotificationItem>>.NotFound("Order not found.");
        }

        var outboxIds = await dbContext.OutboxMessages.AsNoTracking()
            .Where(m => m.TenantId == context.TenantId && m.Content.Contains(normalizedId))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (outboxIds.Count == 0)
        {
            return Result<IReadOnlyList<OrderNotificationItem>>.Success([]);
        }

        var notifications = await dbContext.NotificationRequests.AsNoTracking()
            .Where(n => n.TenantId == context.TenantId && outboxIds.Contains(n.SourceEventId))
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = notifications.Select(n => new OrderNotificationItem(
            n.Id,
            n.TemplateCode,
            n.Channel,
            n.Status,
            PiiRedaction.RedactContact(n.RecipientContact, n.Channel),
            n.AttemptCount,
            n.DeliveredAt,
            n.DeadLetteredAt,
            n.CreatedAt)).ToList();

        return Result<IReadOnlyList<OrderNotificationItem>>.Success(result);
    }

    private static string Require(string value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", paramName);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength) throw new ArgumentException($"Value cannot exceed {maxLength} characters.", paramName);
        return trimmed;
    }
}

