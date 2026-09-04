using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Inventory;
using Kreyora.Application.Models;
using Kreyora.Application.Orders;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Common;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Storefront;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kreyora.Infrastructure.Orders;

public sealed class OrderCreationService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IOrderInventoryReservationService inventory,
    IAuditEventService auditEvents,
    Domain.Abstractions.ITimeProvider timeProvider) : IOrderCreationService
{
    private const string CreateOperation = "order.create";
    private const int MaxSerializableAttempts = 5;

    public async Task<Result<OrderCreationResult>> CreateFromCheckoutAsync(CreateOrderFromCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        tenantContext.RequireCurrent();
        try
        {
            request = Normalize(request);
            for (var attempt = 1; attempt <= MaxSerializableAttempts; attempt++)
            {
                try
                {
                    return await CreateOnceAsync(request, cancellationToken);
                }
                catch (PostgresException exception) when (IsRetryable(exception) && attempt < MaxSerializableAttempts)
                {
                    dbContext.ChangeTracker.Clear();
                    await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt + Random.Shared.Next(10)), cancellationToken);
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxSerializableAttempts)
                {
                    dbContext.ChangeTracker.Clear();
                    await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt + Random.Shared.Next(10)), cancellationToken);
                }
            }

            return Result<OrderCreationResult>.Conflict("Order creation conflicted with another checkout update. Please retry.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            return Result<OrderCreationResult>.Conflict("This checkout session has already created an order.");
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            return Result<OrderCreationResult>.ValidationError(exception.Message);
        }
    }

    private async Task<Result<OrderCreationResult>> CreateOnceAsync(CreateOrderFromCheckoutRequest request, CancellationToken cancellationToken)
    {
        var context = tenantContext.RequireCurrent();
        var fingerprint = Fingerprint(new { request.CheckoutSessionId, request.PaymentMethod });
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await FindReplayAsync(request.IdempotencyKey, fingerprint, cancellationToken);
        if (replay is not null) return replay;

        var session = await dbContext.CheckoutSessions.Include(item => item.Items).SingleOrDefaultAsync(item => item.Id == request.CheckoutSessionId, cancellationToken);
        if (session is null) return Result<OrderCreationResult>.NotFound("The checkout session is unavailable.");
        var now = timeProvider.UtcNow;
        if (session.State != CheckoutSessionState.Active || session.ExpiresAt <= now) return Result<OrderCreationResult>.Conflict("The checkout session is no longer active.");
        if (session.Items.Count is 0 or > 50) return Result<OrderCreationResult>.ValidationError("The checkout session has no valid items.");
        if (request.PaymentMethod == OrderPaymentMethod.CashOnDelivery && !session.CodAvailable) return Result<OrderCreationResult>.ValidationError("Cash on delivery is unavailable for this checkout session.");

        var order = Order.Create(new OrderCreation(context.TenantId, session.StoreId, session.Id, session.CustomerId, request.PaymentMethod,
            session.CustomerName, session.CustomerPhone, session.CustomerEmail, session.AddressLine1, session.AddressLine2, session.District,
            session.Municipality, session.Locality, session.Landmark, session.MerchandiseSubtotalNpr, session.DiscountNpr, session.DeliveryFeeNpr,
            session.TaxNpr, session.ProviderFeeNpr, session.PlatformFeeNpr, session.TotalNpr, session.Currency, session.DeliveryRuleId,
            session.DeliveryRuleName, session.EstimatedEtaText, session.CodAvailable));
        foreach (var source in session.Items)
        {
            order.AddItem(OrderItem.Create(context.TenantId, order.Id, source.InventoryReservationId, source.ProductId, source.ProductTitle,
                source.VariantId, source.VariantName, source.Quantity, source.UnitPriceNpr));
        }

        dbContext.Orders.Add(order);
        var committed = await inventory.CommitForOrderAsync(new OrderInventoryCommitRequest(order.Id, session.Id,
            session.Items.Select(item => new OrderInventoryCommitLine(item.InventoryReservationId, item.VariantId, item.Quantity)).ToArray()), cancellationToken);
        if (committed.IsFailure)
        {
            var error = committed.Error!;
            dbContext.ChangeTracker.Clear();
            return Result<OrderCreationResult>.Failure(error);
        }

        session.Complete(now);
        dbContext.OrderCommands.Add(OrderCommand.Create(context.TenantId, CreateOperation, request.IdempotencyKey, fingerprint, order.Id));
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            TenantId = context.TenantId,
            Type = "order.created.v1",
            Content = JsonSerializer.Serialize(new { orderId = order.Id, checkoutSessionId = session.Id, storeId = order.StoreId, paymentMethod = order.PaymentMethod, orderStatus = order.Status, paymentStatus = order.PaymentStatus })
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEvents.AppendAsync(new AuditEventWrite("order.created", "order", order.Id,
            Metadata: $"{{\"checkoutSessionId\":\"{session.Id}\",\"lineCount\":{order.Items.Count},\"paymentMethod\":\"{order.PaymentMethod}\"}}",
            ActorKind: CommerceActorKind.CommerceSystem), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<OrderCreationResult>.Success(Map(order, false));
    }

    private async Task<Result<OrderCreationResult>?> FindReplayAsync(string idempotencyKey, string fingerprint, CancellationToken cancellationToken)
    {
        var command = await dbContext.OrderCommands.SingleOrDefaultAsync(item => item.Operation == CreateOperation && item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (command is null) return null;
        if (!string.Equals(command.RequestFingerprint, fingerprint, StringComparison.Ordinal)) return Result<OrderCreationResult>.Conflict("The idempotency key was already used for a different order request.");
        var order = await dbContext.Orders.SingleAsync(item => item.Id == command.OrderId, cancellationToken);
        return Result<OrderCreationResult>.Success(Map(order, true));
    }

    private static OrderCreationResult Map(Order order, bool replayed) => new(order.Id, order.OrderNumber, order.CheckoutSessionId, order.Status,
        order.PaymentStatus, order.FulfilmentStatus, order.PaymentMethod, order.TotalNpr, order.Currency, replayed);
    private static CreateOrderFromCheckoutRequest Normalize(CreateOrderFromCheckoutRequest request) => new(
        Require(request.CheckoutSessionId, nameof(request.CheckoutSessionId), 26),
        Enum.IsDefined(request.PaymentMethod) ? request.PaymentMethod : throw new ArgumentOutOfRangeException(nameof(request)),
        Require(request.IdempotencyKey, nameof(request.IdempotencyKey), 256));
    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }
    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static bool IsRetryable(PostgresException exception) => exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;
}
