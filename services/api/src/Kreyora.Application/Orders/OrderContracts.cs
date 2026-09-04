using Kreyora.Application.Models;
using Kreyora.Domain.Orders;

namespace Kreyora.Application.Orders;

public interface IOrderCreationService
{
    Task<Result<OrderCreationResult>> CreateFromCheckoutAsync(CreateOrderFromCheckoutRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreateOrderFromCheckoutRequest(string CheckoutSessionId, OrderPaymentMethod PaymentMethod, string IdempotencyKey);
public sealed record OrderCreationResult(string Id, string OrderNumber, string CheckoutSessionId, OrderStatus Status, PaymentStatus PaymentStatus, FulfilmentStatus FulfilmentStatus, OrderPaymentMethod PaymentMethod, decimal TotalNpr, string Currency, bool WasReplayed);
