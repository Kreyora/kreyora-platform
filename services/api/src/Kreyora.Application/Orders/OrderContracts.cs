using Kreyora.Application.Models;
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
    string IdempotencyKey);

public sealed record OrderOperationResult(
    string OrderId,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfilmentStatus FulfilmentStatus,
    uint RowVersion,
    bool WasReplayed);
