using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Inventory;
using Kreyora.Application.Models;
using Kreyora.Application.Orders;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Payments;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kreyora.Infrastructure.Orders;

public sealed class OrderOperationService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer authorizer,
    IOrderInventoryReservationService inventory,
    IAuditEventService auditEvents,
    Domain.Abstractions.ITimeProvider timeProvider) : IOrderOperationService
{
    private const int MaxAttempts = 5;

    public async Task<Result<IReadOnlyList<OrderActionEvaluation>>> GetAllowedActionsAsync(string orderId, CancellationToken cancellationToken = default)
    {
        authorizer.Demand(TenantPermissions.OrdersRead);
        var context = tenantContext.RequireCurrent();

        var normalizedId = Require(orderId, nameof(orderId), 26);
        var order = await dbContext.Orders
            .SingleOrDefaultAsync(o => o.Id == normalizedId, cancellationToken);

        if (order is null)
        {
            return Result<IReadOnlyList<OrderActionEvaluation>>.NotFound("Order not found.");
        }

        var evaluations = OrderTransitionPolicy.GetAllowedActions(order, context.Role);
        return Result<IReadOnlyList<OrderActionEvaluation>>.Success(evaluations);
    }

    public async Task<Result<OrderOperationResult>> ExecuteActionAsync(ExecuteOrderActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = tenantContext.RequireCurrent();

        // 1. Demand Role Permission
        if (request.Action is OrderAction.VerifyPayment or OrderAction.RejectPayment or OrderAction.MarkCodCollected)
        {
            authorizer.Demand(TenantPermissions.PaymentsManage);
        }
        else
        {
            authorizer.Demand(TenantPermissions.OrdersWrite);
        }

        var normalized = Normalize(request);
        var operation = $"order.{normalized.Action.ToString().ToLowerInvariant()}";
        var fingerprint = Fingerprint(new
        {
            normalized.OrderId,
            action = normalized.Action.ToString(),
            reason = normalized.Reason ?? string.Empty,
            paymentAttemptId = normalized.PaymentAttemptId ?? string.Empty,
            providerReference = normalized.ProviderReference ?? string.Empty
        });

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                // 2. Check Idempotency Replay
                var existingCommand = await dbContext.OrderCommands
                    .SingleOrDefaultAsync(c => c.Operation == operation && c.IdempotencyKey == normalized.IdempotencyKey, cancellationToken);

                if (existingCommand is not null)
                {
                    if (!string.Equals(existingCommand.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        return Result<OrderOperationResult>.Conflict("The idempotency key was already used for a different order action.");
                    }

                    var replayedOrder = await dbContext.Orders
                        .SingleOrDefaultAsync(o => o.Id == existingCommand.OrderId, cancellationToken);

                    if (replayedOrder is null)
                    {
                        return Result<OrderOperationResult>.NotFound("Order not found.");
                    }

                    var replayedVersion = dbContext.Entry(replayedOrder).Property<uint>("xmin").CurrentValue;
                    return Result<OrderOperationResult>.Success(new OrderOperationResult(
                        replayedOrder.Id,
                        replayedOrder.OrderNumber,
                        replayedOrder.Status,
                        replayedOrder.PaymentStatus,
                        replayedOrder.FulfilmentStatus,
                        replayedVersion,
                        true));
                }

                // 3. Load Order and check tenant ownership
                var order = await dbContext.Orders
                    .Include(o => o.Items)
                    .SingleOrDefaultAsync(o => o.Id == normalized.OrderId, cancellationToken);

                if (order is null)
                {
                    return Result<OrderOperationResult>.NotFound("Order not found.");
                }

                // 4. Concurrency check
                dbContext.Entry(order).Property<uint>("xmin").OriginalValue = normalized.ExpectedVersion;

                // 5. Evaluate Transition Policy
                if (!OrderTransitionPolicy.CanExecute(order, normalized.Action, context.Role, out var denialReason))
                {
                    return Result<OrderOperationResult>.ValidationError(denialReason!);
                }

                if (!OrderTransitionPolicy.ValidateReason(normalized.Action, normalized.Reason, out var reasonError))
                {
                    return Result<OrderOperationResult>.ValidationError(reasonError!);
                }

                // 6. Capture Prior States
                var priorStatus = order.Status;
                var priorPaymentStatus = order.PaymentStatus;
                var priorFulfilmentStatus = order.FulfilmentStatus;
                var now = timeProvider.UtcNow;

                // 7. Execute Aggregate Transition
                switch (normalized.Action)
                {
                    case OrderAction.Confirm:
                        order.Confirm(now);
                        break;
                    case OrderAction.Cancel:
                        order.Cancel(normalized.Reason!, now);
                        if (order.Items.Count > 0)
                        {
                            var restockLines = order.Items
                                .Select(item => new OrderInventoryRestockLine(item.VariantId, item.Quantity))
                                .ToArray();
                            var restockResult = await inventory.RestockForOrderAsync(
                                new OrderInventoryRestockRequest(order.Id, normalized.Reason!, restockLines),
                                cancellationToken);
                            if (restockResult.IsFailure)
                            {
                                dbContext.ChangeTracker.Clear();
                                return Result<OrderOperationResult>.Failure(restockResult.Error!);
                            }
                        }

                        var pendingAttempts = await dbContext.PaymentAttempts
                            .Where(pa => pa.OrderId == order.Id && (pa.Status == PaymentAttemptStatus.Pending || pa.Status == PaymentAttemptStatus.AwaitingProof || pa.Status == PaymentAttemptStatus.ProofSubmitted))
                            .ToListAsync(cancellationToken);
                        foreach (var pa in pendingAttempts)
                        {
                            pa.Expire(now);
                        }
                        break;
                    case OrderAction.Prepare:
                        order.Prepare(now);
                        break;
                    case OrderAction.Dispatch:
                        order.Dispatch(now);
                        break;
                    case OrderAction.Deliver:
                        order.Deliver(now);
                        break;
                    case OrderAction.MarkDeliveryFailed:
                        order.MarkDeliveryFailed(normalized.Reason!, now);
                        break;
                    case OrderAction.VerifyPayment:
                        order.VerifyPayment(now);
                        {
                            var paymentAttempt = await GetTargetPaymentAttemptAsync(dbContext, order.Id, normalized.PaymentAttemptId, OrderPaymentMethod.MerchantQr, cancellationToken);
                            paymentAttempt?.Verify(context.UserId ?? "system", now, normalized.ProviderReference);
                        }
                        break;
                    case OrderAction.RejectPayment:
                        order.RejectPayment(normalized.Reason!, now);
                        {
                            var paymentAttempt = await GetTargetPaymentAttemptAsync(dbContext, order.Id, normalized.PaymentAttemptId, OrderPaymentMethod.MerchantQr, cancellationToken);
                            paymentAttempt?.Reject(context.UserId ?? "system", normalized.Reason!, now);
                        }
                        break;
                    case OrderAction.MarkCodCollected:
                        order.MarkCodCollected(now);
                        {
                            var paymentAttempt = await GetTargetPaymentAttemptAsync(dbContext, order.Id, normalized.PaymentAttemptId, OrderPaymentMethod.CashOnDelivery, cancellationToken);
                            paymentAttempt?.MarkCollected(context.UserId ?? "system", now, normalized.ProviderReference);
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported order action: {normalized.Action}");
                }

                // 8. Record Idempotency Command
                dbContext.OrderCommands.Add(OrderCommand.Create(
                    context.TenantId,
                    operation,
                    normalized.IdempotencyKey,
                    fingerprint,
                    order.Id));

                // 9. Record Outbox Message
                var outboxType = normalized.Action switch
                {
                    OrderAction.Confirm => "order.confirmed.v1",
                    OrderAction.Cancel => "order.cancelled.v1",
                    OrderAction.Prepare => "order.prepared.v1",
                    OrderAction.Dispatch => "order.dispatched.v1",
                    OrderAction.Deliver => "order.delivered.v1",
                    OrderAction.MarkDeliveryFailed => "order.delivery_failed.v1",
                    OrderAction.VerifyPayment => "payment.verified.v1",
                    OrderAction.RejectPayment => "payment.rejected.v1",
                    OrderAction.MarkCodCollected => "payment.cod_collected.v1",
                    _ => null
                };

                if (outboxType is not null)
                {
                    dbContext.OutboxMessages.Add(new OutboxMessage
                    {
                        TenantId = context.TenantId,
                        Type = outboxType,
                        Content = JsonSerializer.Serialize(new
                        {
                            orderId = order.Id,
                            orderNumber = order.OrderNumber,
                            storeId = order.StoreId,
                            action = normalized.Action.ToString(),
                            priorStatus,
                            newStatus = order.Status,
                            priorPaymentStatus,
                            newPaymentStatus = order.PaymentStatus,
                            priorFulfilmentStatus,
                            newFulfilmentStatus = order.FulfilmentStatus,
                            reason = normalized.Reason,
                            timestamp = now
                        })
                    });
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                // 9. Record Audit Event
                var auditMetadata = JsonSerializer.Serialize(new
                {
                    priorStatus,
                    newStatus = order.Status,
                    priorPaymentStatus,
                    newPaymentStatus = order.PaymentStatus,
                    priorFulfilmentStatus,
                    newFulfilmentStatus = order.FulfilmentStatus
                });

                await auditEvents.AppendAsync(new AuditEventWrite(
                    Action: operation,
                    TargetType: "order",
                    TargetId: order.Id,
                    Reason: normalized.Reason,
                    Metadata: auditMetadata), cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                var currentVersion = dbContext.Entry(order).Property<uint>("xmin").CurrentValue;
                return Result<OrderOperationResult>.Success(new OrderOperationResult(
                    order.Id,
                    order.OrderNumber,
                    order.Status,
                    order.PaymentStatus,
                    order.FulfilmentStatus,
                    currentVersion,
                    false));
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                return Result<OrderOperationResult>.Conflict("The order has been modified by another operation. Please reload and retry.");
            }
            catch (PostgresException exception) when (IsRetryable(exception) && attempt < MaxAttempts)
            {
                dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt + Random.Shared.Next(10)), cancellationToken);
            }
            catch (InvalidOperationException exception) when (IsTransientFailure(exception) && attempt < MaxAttempts)
            {
                dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt + Random.Shared.Next(10)), cancellationToken);
            }
            catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                dbContext.ChangeTracker.Clear();
                return Result<OrderOperationResult>.ValidationError(exception.Message);
            }
        }

        return Result<OrderOperationResult>.Conflict("Order operation conflicted with another concurrent update. Please retry.");
    }

    private static ExecuteOrderActionRequest Normalize(ExecuteOrderActionRequest request) => new(
        Require(request.OrderId, nameof(request.OrderId), 26),
        Enum.IsDefined(request.Action) ? request.Action : throw new ArgumentOutOfRangeException(nameof(request), "Invalid order action."),
        Optional(request.Reason, 500),
        request.ExpectedVersion,
        Require(request.IdempotencyKey, nameof(request.IdempotencyKey), 256),
        Optional(request.PaymentAttemptId, 26),
        Optional(request.ProviderReference, 128));

    private static async Task<PaymentAttempt?> GetTargetPaymentAttemptAsync(
        AppDbContext dbContext,
        string orderId,
        string? specifiedAttemptId,
        OrderPaymentMethod method,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(specifiedAttemptId))
        {
            return await dbContext.PaymentAttempts
                .SingleOrDefaultAsync(a => a.Id == specifiedAttemptId && a.OrderId == orderId, cancellationToken);
        }

        return await dbContext.PaymentAttempts
            .Where(a => a.OrderId == orderId && a.Method == method)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Require(value, nameof(value), maximumLength);

    private static string Fingerprint<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private static bool IsRetryable(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;

    private static bool IsTransientFailure(InvalidOperationException exception) =>
        exception.Message.Contains("transient failure", StringComparison.OrdinalIgnoreCase);
}
