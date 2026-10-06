using Kreyora.Application.Models;
using Kreyora.Application.Payments;
using Kreyora.Domain.Notifications;
using Kreyora.Domain.Orders;

namespace Kreyora.Application.Orders;

public interface IOrderCreationService
{
    Task<Result<OrderCreationResult>> CreateFromCheckoutAsync(CreateOrderFromCheckoutRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreateOrderFromCheckoutRequest(string CheckoutSessionId, OrderPaymentMethod PaymentMethod, string IdempotencyKey);
public sealed record OrderCreationResult(string Id, string OrderNumber, string CheckoutSessionId, OrderStatus Status, PaymentStatus PaymentStatus, FulfilmentStatus FulfilmentStatus, OrderPaymentMethod PaymentMethod, decimal TotalNpr, string Currency, bool WasReplayed);

public interface IOrderOperationService
{
    Task<Result<IReadOnlyList<OrderActionEvaluation>>> GetAllowedActionsAsync(string orderId, CancellationToken cancellationToken = default);
    Task<Result<OrderOperationResult>> ExecuteActionAsync(ExecuteOrderActionRequest request, CancellationToken cancellationToken = default);
}

public sealed record ExecuteOrderActionRequest(
    string OrderId,
    OrderAction Action,
    string? Reason,
    uint ExpectedVersion,
    string IdempotencyKey,
    string? PaymentAttemptId = null,
    string? ProviderReference = null);

public sealed record OrderOperationResult(
    string OrderId,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfilmentStatus FulfilmentStatus,
    uint RowVersion,
    bool WasReplayed);

public interface IOrderQueryService
{
    Task<Result<PagedResult<OrderSummaryItem>>> ListOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default);
    Task<Result<OrderDetailItem>> GetOrderDetailAsync(string orderId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<OrderActivityItem>>> GetOrderActivityAsync(string orderId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<OrderNotificationItem>>> GetOrderNotificationsAsync(string orderId, CancellationToken cancellationToken = default);
}

public sealed record OrderQuery(
    int Page = 1,
    int PageSize = 20,
    OrderStatus? Status = null,
    PaymentStatus? PaymentStatus = null,
    FulfilmentStatus? FulfilmentStatus = null,
    OrderPaymentMethod? PaymentMethod = null,
    OrderSource? Source = null,
    string? Search = null);

public sealed record OrderSummaryItem(
    string Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfilmentStatus FulfilmentStatus,
    OrderPaymentMethod PaymentMethod,
    OrderSource Source,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string District,
    decimal TotalNpr,
    string Currency,
    int ItemCount,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

public sealed record OrderDetailItem(
    string Id,
    string OrderNumber,
    string StoreId,
    string CheckoutSessionId,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfilmentStatus FulfilmentStatus,
    OrderPaymentMethod PaymentMethod,
    OrderSource Source,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string? Municipality,
    string? Locality,
    string? Landmark,
    decimal MerchandiseSubtotalNpr,
    decimal DiscountNpr,
    decimal DeliveryFeeNpr,
    decimal TaxNpr,
    decimal TotalNpr,
    string Currency,
    string DeliveryRuleId,
    string DeliveryRuleName,
    string? EstimatedEtaText,
    bool CodAvailable,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    IReadOnlyList<OrderItemDetail> Items,
    IReadOnlyList<PaymentAttemptItem> PaymentAttempts);

public sealed record OrderItemDetail(
    string Id,
    string ProductId,
    string ProductTitle,
    string VariantId,
    string VariantName,
    decimal UnitPriceNpr,
    int Quantity,
    decimal LineTotalNpr,
    string Currency);

public sealed record OrderActivityItem(
    string Id,
    string Action,
    string? ActorUserId,
    DateTimeOffset OccurredAt,
    string? Reason,
    string? Details);

public sealed record OrderNotificationItem(
    string Id,
    string TemplateCode,
    NotificationChannel Channel,
    NotificationStatus Status,
    string RecipientContactRedacted,
    int AttemptCount,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? DeadLetteredAt,
    DateTimeOffset CreatedAt);

// ---- Customer order-status lookup for the assistant (M09-S04 Q4) -------------------------------------------------

public enum OrderStatusLookupOutcome
{
    Found,
    VerificationRequired,
    NotVerified,
    Locked
}

/// <summary>Minimal order status; never name, phone, address or totals.</summary>
public sealed record CustomerOrderStatus(
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfilmentStatus FulfilmentStatus,
    DateTimeOffset PlacedAt,
    int ItemCount,
    string? EtaText);

public sealed record OrderStatusLookupRequest(string? CustomerId, string? ConversationId, string? OrderNumber, string? PhoneLast4);

public sealed record OrderStatusLookupResult(OrderStatusLookupOutcome Outcome, IReadOnlyList<CustomerOrderStatus> Orders);

/// <summary>
/// Order status for a chat customer: orders linked to the customer, or one order proven by its number plus the last
/// four digits of its phone. Wrong pairs answer uniformly; five failures per order in 24 hours lock chat lookups.
/// </summary>
public interface IOrderStatusLookupService
{
    Task<OrderStatusLookupResult> LookupAsync(OrderStatusLookupRequest request, CancellationToken cancellationToken = default);
}
