using System.Text.Json;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Inventory;
using Kreyora.Application.Notifications;
using Kreyora.Application.Orders;
using Kreyora.Application.Payments;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Common;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Notifications;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Payments;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Catalog;
using Kreyora.Infrastructure.Customers;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Notifications;
using Kreyora.Infrastructure.Orders;
using Kreyora.Infrastructure.Payments;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Kreyora.Infrastructure.Storefront;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Orders;

public sealed class Milestone06LifecycleAndFailureTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public Milestone06LifecycleAndFailureTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task Scenario1_CompleteCodOrderLifecycle_StockCommitted_Reconciled_AuditAndOutboxEmitted()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "m06-cod-lifecycle");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "cod-store", 10, true);

        // 1. Checkout -> Order created in PendingConfirmation / Pending / Unfulfilled
        var order = await CreateOrderFromCheckoutAsync(
            db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 2, OrderPaymentMethod.CashOnDelivery, "cod-idem-1");

        Assert.Equal(OrderStatus.PendingConfirmation, order.Status);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Unfulfilled, order.FulfilmentStatus);

        // Verify stock ledger post-checkout commitment: onHand=8, reserved=0, available=8
        var invItem = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(8, invItem.OnHandQuantity);
        Assert.Equal(0, invItem.ReservedQuantity);
        Assert.Equal(8, invItem.AvailableQuantity);

        var operationService = CreateOrderOperationService(db, accessor, clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        // 2. Operator confirms order
        var detail1 = await queryService.GetOrderDetailAsync(order.Id);
        var confirmRes = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Confirm, null, detail1.Value!.RowVersion, "cod-confirm-key"));
        Assert.True(confirmRes.IsSuccess);
        Assert.Equal(OrderStatus.Confirmed, confirmRes.Value!.Status);

        db.ChangeTracker.Clear();

        // 3. Operator prepares order (Ready)
        var detail2 = await queryService.GetOrderDetailAsync(order.Id);
        var prepareRes = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Prepare, null, detail2.Value!.RowVersion, "cod-prepare-key"));
        Assert.True(prepareRes.IsSuccess);
        Assert.Equal(FulfilmentStatus.Ready, prepareRes.Value!.FulfilmentStatus);
        Assert.Equal(OrderStatus.Processing, prepareRes.Value.Status);

        db.ChangeTracker.Clear();

        // 4. Operator dispatches order
        var detail3 = await queryService.GetOrderDetailAsync(order.Id);
        var dispatchRes = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Dispatch, null, detail3.Value!.RowVersion, "cod-dispatch-key"));
        Assert.True(dispatchRes.IsSuccess);
        Assert.Equal(FulfilmentStatus.Dispatched, dispatchRes.Value!.FulfilmentStatus);

        db.ChangeTracker.Clear();

        // 5. Operator delivers order
        var detail4 = await queryService.GetOrderDetailAsync(order.Id);
        var deliverRes = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Deliver, null, detail4.Value!.RowVersion, "cod-deliver-key"));
        Assert.True(deliverRes.IsSuccess);
        Assert.Equal(FulfilmentStatus.Delivered, deliverRes.Value!.FulfilmentStatus);

        db.ChangeTracker.Clear();

        // 6. Operator marks COD collected -> PaymentStatus.Paid, OrderStatus.Fulfilled
        var detail5 = await queryService.GetOrderDetailAsync(order.Id);
        var codCollectRes = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.MarkCodCollected, null, detail5.Value!.RowVersion, "cod-collected-key"));
        Assert.True(codCollectRes.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, codCollectRes.Value!.PaymentStatus);
        Assert.Equal(OrderStatus.Fulfilled, codCollectRes.Value.Status);

        db.ChangeTracker.Clear();

        // Verify stock ledger reconciliation: exactly 2 units committed, balances unchanged
        var finalInv = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(8, finalInv.OnHandQuantity);
        Assert.Equal(0, finalInv.ReservedQuantity);
        Assert.Equal(8, finalInv.AvailableQuantity);

        // Verify audit trail
        var activities = await queryService.GetOrderActivityAsync(order.Id);
        Assert.True(activities.IsSuccess);
        var actions = activities.Value!.Select(a => a.Action).ToList();
        Assert.Contains("order.confirm", actions);
        Assert.Contains("order.prepare", actions);
        Assert.Contains("order.dispatch", actions);
        Assert.Contains("order.deliver", actions);
        Assert.Contains("order.markcodcollected", actions);

        // Verify outbox messages were generated across lifecycle transitions
        var outboxMessages = await db.OutboxMessages.Where(m => m.TenantId == tenant.Id).ToListAsync();
        var types = outboxMessages.Select(m => m.Type).ToList();
        Assert.Contains("order.confirmed.v1", types);
        Assert.Contains("order.dispatched.v1", types);
        Assert.Contains("order.delivered.v1", types);
    }

    [Fact]
    public async Task Scenario2_CompleteMerchantQrLifecycle_ProofUploaded_Verified_Delivered()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "m06-qr-lifecycle");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "qr-store", 10, false);

        // 1. Checkout -> Order created in PendingConfirmation / AwaitingVerification / Unfulfilled
        var order = await CreateOrderFromCheckoutAsync(
            db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 1, OrderPaymentMethod.MerchantQr, "qr-idem-1");

        Assert.Equal(OrderStatus.PendingConfirmation, order.Status);
        Assert.Equal(PaymentStatus.AwaitingVerification, order.PaymentStatus);

        var attempt = await db.PaymentAttempts.SingleAsync(pa => pa.OrderId == order.Id);
        Assert.Equal(PaymentAttemptStatus.AwaitingProof, attempt.Status);

        // 2. Customer uploads valid JPEG proof (magic bytes FF D8 FF E0)
        var proof = PaymentProof.CreatePending(
            tenant.Id,
            attempt.Id,
            "proofs/cust-slip.jpg",
            "image/jpeg",
            4096,
            DateTimeOffset.UtcNow.AddHours(2),
            "Fonepay transaction slip");
        proof.Complete(DateTimeOffset.UtcNow);
        db.PaymentProofs.Add(proof);
        attempt.AddProof(proof, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        Assert.Equal(PaymentAttemptStatus.ProofSubmitted, attempt.Status);

        var operationService = CreateOrderOperationService(db, accessor, clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        // 3. Operator reviews proof and verifies payment
        var detail1 = await queryService.GetOrderDetailAsync(order.Id);
        var verifyRes = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.VerifyPayment, null, detail1.Value!.RowVersion, "verify-key-1", attempt.Id));
        Assert.True(verifyRes.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, verifyRes.Value!.PaymentStatus);

        db.ChangeTracker.Clear();

        // 4. Operator confirms, prepares, dispatches, delivers
        var d2 = await queryService.GetOrderDetailAsync(order.Id);
        await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, d2.Value!.RowVersion, "qr-confirm-key"));

        db.ChangeTracker.Clear();
        var d3 = await queryService.GetOrderDetailAsync(order.Id);
        await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Prepare, null, d3.Value!.RowVersion, "qr-prepare-key"));

        db.ChangeTracker.Clear();
        var d4 = await queryService.GetOrderDetailAsync(order.Id);
        await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Dispatch, null, d4.Value!.RowVersion, "qr-dispatch-key"));

        db.ChangeTracker.Clear();
        var d5 = await queryService.GetOrderDetailAsync(order.Id);
        var deliverRes = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Deliver, null, d5.Value!.RowVersion, "qr-deliver-key"));
        Assert.True(deliverRes.IsSuccess);
        Assert.Equal(OrderStatus.Fulfilled, deliverRes.Value!.Status);
        Assert.Equal(FulfilmentStatus.Delivered, deliverRes.Value.FulfilmentStatus);

        db.ChangeTracker.Clear();

        // Verify payment attempt marked verified
        var updatedAttempt = await db.PaymentAttempts.SingleAsync(pa => pa.Id == attempt.Id);
        Assert.Equal(PaymentAttemptStatus.Verified, updatedAttempt.Status);
        Assert.NotNull(updatedAttempt.VerifiedAt);
    }

    [Fact]
    public async Task Scenario3_MerchantQr_ProofRejected_OrderCancelled_AutomatedRestockReconciled()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "m06-reject-restock");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "restock-store", 5, false);

        // 1. Order placed for 2 units -> onHand moves from 5 to 3
        var order = await CreateOrderFromCheckoutAsync(
            db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 2, OrderPaymentMethod.MerchantQr, "restock-idem-1");

        var invBefore = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(3, invBefore.OnHandQuantity);
        Assert.Equal(0, invBefore.ReservedQuantity);
        Assert.Equal(3, invBefore.AvailableQuantity);

        var attempt = await db.PaymentAttempts.SingleAsync(pa => pa.OrderId == order.Id);

        // 2. Customer uploads proof
        var proof = PaymentProof.CreatePending(tenant.Id, attempt.Id, "proofs/fake.jpg", "image/jpeg", 1024, DateTimeOffset.UtcNow.AddHours(1), null);
        proof.Complete(DateTimeOffset.UtcNow);
        db.PaymentProofs.Add(proof);
        attempt.AddProof(proof, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var operationService = CreateOrderOperationService(db, accessor, clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        // 3. Operator rejects payment proof with reason
        var d1 = await queryService.GetOrderDetailAsync(order.Id);
        var rejectRes = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.RejectPayment, "Unreadable screenshot with wrong amount", d1.Value!.RowVersion, "reject-key-1", attempt.Id));
        Assert.True(rejectRes.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, rejectRes.Value!.PaymentStatus);

        db.ChangeTracker.Clear();

        // 4. Operator cancels order with reason -> triggers automated restock
        var d2 = await queryService.GetOrderDetailAsync(order.Id);
        var cancelRes = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Customer failed to provide valid payment proof", d2.Value!.RowVersion, "cancel-key-1"));
        Assert.True(cancelRes.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, cancelRes.Value!.Status);

        db.ChangeTracker.Clear();

        // 5. Verify inventory restocked: onHand = 5, reserved = 0, available = 5
        var invAfter = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(5, invAfter.OnHandQuantity);
        Assert.Equal(0, invAfter.ReservedQuantity);
        Assert.Equal(5, invAfter.AvailableQuantity);

        // Verify StockMovement recorded with OrderRestock type
        var movements = await db.StockMovements
            .Where(m => m.TenantId == tenant.Id && m.VariantId == seeded.FirstVariantId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
        var restockMovement = Assert.Single(movements, m => m.Type == StockMovementType.OrderRestock);
        Assert.Equal(2, restockMovement.QuantityDelta);
        Assert.Contains(order.Id, restockMovement.Reason);

        // Verify payment attempt is expired
        var updatedAttempt = await db.PaymentAttempts.SingleAsync(pa => pa.Id == attempt.Id);
        Assert.Equal(PaymentAttemptStatus.Rejected, updatedAttempt.Status);
    }

    [Fact]
    public async Task Scenario4_OptimisticConcurrency_StaleVersion_Returns409Conflict_RecoverySafe()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "m06-concurrency");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery, "Concurrency Customer", "+9779800000001");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var operationService = CreateOrderOperationService(db, accessor, clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        // Operator A and B both read version V1
        var initialDetail = await queryService.GetOrderDetailAsync(order.Id);
        var versionV1 = initialDetail.Value!.RowVersion;

        // Operator A confirms -> version moves to V2
        var resA = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Confirm, null, versionV1, "op-a-confirm"));
        Assert.True(resA.IsSuccess);
        var versionV2 = resA.Value!.RowVersion;
        Assert.NotEqual(versionV1, versionV2);

        db.ChangeTracker.Clear();

        // Operator B attempts to cancel with stale version V1 -> 409 Conflict
        var resB = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Customer changed mind", versionV1, "op-b-cancel"));
        Assert.True(resB.IsFailure);
        Assert.Equal(409, resB.Error!.Status);

        db.ChangeTracker.Clear();

        // Operator B recovers by refreshing latest state -> receives V2, sees Confirmed
        var refreshedDetail = await queryService.GetOrderDetailAsync(order.Id);
        Assert.Equal(versionV2, refreshedDetail.Value!.RowVersion);
        Assert.Equal(OrderStatus.Confirmed, refreshedDetail.Value.Status);
    }

    [Fact]
    public async Task Scenario5_ConcurrentTerminalCollision_CancelVsDispatch_ExactlyOneWins_ZeroLeakage()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "m06-terminal-race");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "race-store", 10, true);

        var order = await CreateOrderFromCheckoutAsync(
            db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 2, OrderPaymentMethod.CashOnDelivery, "race-idem-1");

        var operationService = CreateOrderOperationService(db, accessor, clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        // Move to Ready
        var d1 = await queryService.GetOrderDetailAsync(order.Id);
        await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, d1.Value!.RowVersion, "race-conf"));
        db.ChangeTracker.Clear();

        var d2 = await queryService.GetOrderDetailAsync(order.Id);
        await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Prepare, null, d2.Value!.RowVersion, "race-prep"));
        db.ChangeTracker.Clear();

        var readyDetail = await queryService.GetOrderDetailAsync(order.Id);
        var expectedVersion = readyDetail.Value!.RowVersion;

        // Two independent concurrent tasks try to mutate order: Cancel vs Dispatch
        await using var dbCancel = fixture.CreateDbContext(accessor);
        await using var dbDispatch = fixture.CreateDbContext(accessor);

        var opCancel = CreateOrderOperationService(dbCancel, accessor, clock);
        var opDispatch = CreateOrderOperationService(dbDispatch, accessor, clock);

        var cancelTask = opCancel.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Urgent cancellation request", expectedVersion, "race-cancel-key"));
        var dispatchTask = opDispatch.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Dispatch, null, expectedVersion, "race-dispatch-key"));

        var results = await Task.WhenAll(cancelTask, dispatchTask);
        var cancelResult = results[0];
        var dispatchResult = results[1];

        // Exactly one succeeded, one failed with conflict
        var successCount = (cancelResult.IsSuccess ? 1 : 0) + (dispatchResult.IsSuccess ? 1 : 0);
        Assert.Equal(1, successCount);

        db.ChangeTracker.Clear();
        var finalDetail = await queryService.GetOrderDetailAsync(order.Id);

        var finalInv = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);

        if (cancelResult.IsSuccess)
        {
            Assert.Equal(OrderStatus.Cancelled, finalDetail.Value!.Status);
            // On cancel, stock was restocked: 10 units on hand
            Assert.Equal(10, finalInv.OnHandQuantity);
            Assert.Equal(0, finalInv.ReservedQuantity);
            Assert.Equal(10, finalInv.AvailableQuantity);
        }
        else
        {
            Assert.Equal(FulfilmentStatus.Dispatched, finalDetail.Value!.FulfilmentStatus);
            // On dispatch, stock remained committed: 8 units on hand
            Assert.Equal(8, finalInv.OnHandQuantity);
            Assert.Equal(0, finalInv.ReservedQuantity);
            Assert.Equal(8, finalInv.AvailableQuantity);
        }
    }

    [Fact]
    public async Task Scenario6_IdempotentActionReplay_SameKeyReturnsCachedResult_DifferingPayloadConflicts()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "m06-idempotency");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery, "Idempotent Customer", "+9779800000002");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var operationService = CreateOrderOperationService(db, accessor, clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        var detail = await queryService.GetOrderDetailAsync(order.Id);

        // 1. Initial execution
        var res1 = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Confirm, null, detail.Value!.RowVersion, "shared-idem-key-1"));
        Assert.True(res1.IsSuccess);
        Assert.False(res1.Value!.WasReplayed);
        Assert.Equal(OrderStatus.Confirmed, res1.Value.Status);

        db.ChangeTracker.Clear();

        // 2. Identical replay -> returns cached result
        var res2 = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Confirm, null, detail.Value.RowVersion, "shared-idem-key-1"));
        Assert.True(res2.IsSuccess);
        Assert.True(res2.Value!.WasReplayed);
        Assert.Equal(OrderStatus.Confirmed, res2.Value.Status);

        // Verify only 1 audit event exists for this action
        var activities = await queryService.GetOrderActivityAsync(order.Id);
        var confirmEvents = activities.Value!.Where(a => a.Action == "order.confirm").ToList();
        Assert.Single(confirmEvents);

        db.ChangeTracker.Clear();

        // 3. Different action reusing the same idempotency key -> conflict / validation error
        var res3 = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Different action payload", detail.Value.RowVersion, "shared-idem-key-1"));
        Assert.True(res3.IsFailure);
        Assert.Equal(409, res3.Error!.Status);
    }

    [Fact]
    public async Task Scenario7_NotificationDelivery_BoundedRetries_DeadLettered_ManualReplayAuthorizes()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "m06-notif-dlq");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id, OrderPaymentMethod.CashOnDelivery, "Retry Customer", "+9779800000003");
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        var notification = NotificationRequest.Create(
            tenant.Id,
            "order.confirmed.v1",
            "source-evt-01",
            "order_confirmed",
            1,
            NotificationChannel.Email,
            order.CustomerEmail!,
            order.CustomerName,
            maxAttempts: 3);
        db.NotificationRequests.Add(notification);
        await db.SaveChangesAsync();

        var failingProvider = new FailingNotificationProvider();
        var services = BuildServices(db, accessor, clock, failingProvider);
        var deliveryJob = new NotificationDeliveryJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NotificationDeliveryJob>.Instance);

        // Attempt 1 -> Fails, backoff 1 min
        var d1 = await deliveryJob.DeliverTenantAsync(services, tenant.Id);
        Assert.Equal(1, d1);

        db.ChangeTracker.Clear();
        var n1 = await db.NotificationRequests.SingleAsync(n => n.Id == notification.Id);
        Assert.Equal(NotificationStatus.Failed, n1.Status);
        Assert.Equal(1, n1.AttemptCount);
        Assert.NotNull(n1.NextRetryAt);

        // Advance clock by 30 seconds -> skipped because NextRetryAt has not arrived
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        db.ChangeTracker.Clear();
        var d1_skip = await deliveryJob.DeliverTenantAsync(services, tenant.Id);
        Assert.Equal(0, d1_skip);

        // Advance clock past 1 minute -> Attempt 2 fails
        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        db.ChangeTracker.Clear();
        var d2 = await deliveryJob.DeliverTenantAsync(services, tenant.Id);
        Assert.Equal(1, d2);

        // Advance clock past 5 minutes -> Attempt 3 fails -> DeadLettered!
        clock.UtcNow = clock.UtcNow.AddMinutes(6);
        db.ChangeTracker.Clear();
        var d3 = await deliveryJob.DeliverTenantAsync(services, tenant.Id);
        Assert.Equal(1, d3);

        db.ChangeTracker.Clear();
        var deadLettered = await db.NotificationRequests.Include(n => n.DeliveryAttempts).SingleAsync(n => n.Id == notification.Id);
        Assert.Equal(NotificationStatus.DeadLettered, deadLettered.Status);
        Assert.Equal(3, deadLettered.AttemptCount);
        Assert.NotNull(deadLettered.DeadLetteredAt);
        Assert.Null(deadLettered.NextRetryAt);

        // Verify recipient contact is redacted in query
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);

        // Authorize manual replay: reset status to Pending
        var notifService = services.GetRequiredService<INotificationService>();
        var replayRes = await notifService.ReplayAsync(new ReplayNotificationRequest(notification.Id, "replay-idem-key"));
        Assert.True(replayRes.IsSuccess);

        db.ChangeTracker.Clear();
        var replayed = await db.NotificationRequests.SingleAsync(n => n.Id == notification.Id);
        Assert.Equal(NotificationStatus.Pending, replayed.Status);
        Assert.Null(replayed.DeadLetteredAt);

        // Now deliver with working provider
        var workingServices = BuildServices(db, accessor, clock, null);
        var workingDeliveryJob = new NotificationDeliveryJob(
            workingServices.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NotificationDeliveryJob>.Instance);

        var finalDeliver = await workingDeliveryJob.DeliverTenantAsync(workingServices, tenant.Id);
        Assert.Equal(1, finalDeliver);

        db.ChangeTracker.Clear();
        var delivered = await db.NotificationRequests.SingleAsync(n => n.Id == notification.Id);
        Assert.Equal(NotificationStatus.Delivered, delivered.Status);
        Assert.NotNull(delivered.DeliveredAt);
    }

    [Fact]
    public async Task Scenario8_CrossTenantIsolation_QueriesAndActions_ReturnNotFoundOrForbidden()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenantA = await CreateTenantAsync(db, "m06-iso-a");
        var tenantB = await CreateTenantAsync(db, "m06-iso-b");

        string orderAId;
        string proofAId;
        using (accessor.BeginScope(OwnerContext(tenantA.Id)))
        {
            var orderA = await CreateDirectOrderAsync(db, tenantA.Id, OrderPaymentMethod.MerchantQr, "Customer A", "+9779800000010");
            orderAId = orderA.Id;

            var attemptA = await db.PaymentAttempts.SingleAsync(p => p.OrderId == orderA.Id);
            var proof = PaymentProof.CreatePending(tenantA.Id, attemptA.Id, "proofs/a.jpg", "image/jpeg", 2048, DateTimeOffset.UtcNow.AddHours(1), null);
            proof.Complete(DateTimeOffset.UtcNow);
            db.PaymentProofs.Add(proof);
            attemptA.AddProof(proof, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            proofAId = proof.Id;
        }

        // Switch to Tenant B
        using (accessor.BeginScope(OwnerContext(tenantB.Id)))
        {
            var authorizer = new TenantPermissionAuthorizer(accessor);
            var queryService = new OrderQueryService(db, accessor, authorizer);
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var operationService = CreateOrderOperationService(db, accessor, clock);

            // 1. List orders in Tenant B does not contain Tenant A orders
            var listResult = await queryService.ListOrdersAsync(new OrderQuery());
            Assert.True(listResult.IsSuccess);
            Assert.DoesNotContain(listResult.Value!.Items, o => o.Id == orderAId);

            // 2. Detail query for Tenant A order returns 404
            var detailResult = await queryService.GetOrderDetailAsync(orderAId);
            Assert.True(detailResult.IsFailure);
            Assert.Equal(404, detailResult.Error!.Status);

            // 3. Activity query for Tenant A order returns 404
            var activityResult = await queryService.GetOrderActivityAsync(orderAId);
            Assert.True(activityResult.IsFailure);
            Assert.Equal(404, activityResult.Error!.Status);

            // 4. Notifications query for Tenant A order returns 404
            var notifsResult = await queryService.GetOrderNotificationsAsync(orderAId);
            Assert.True(notifsResult.IsFailure);
            Assert.Equal(404, notifsResult.Error!.Status);

            // 5. Action execution against Tenant A order fails (404 Not Found)
            var actionResult = await operationService.ExecuteActionAsync(new ExecuteOrderActionRequest(
                orderAId, OrderAction.Confirm, null, 1, "illegal-cross-tenant-action"));
            Assert.True(actionResult.IsFailure);
            Assert.Equal(404, actionResult.Error!.Status);
        }
    }

    [Fact]
    public async Task Scenario9_ImmutableOrderSnapshot_CatalogAndDeliveryRuleEdits_DoNotMutateOrder()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "m06-immutable");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "imm-store", 10, true);

        // Place order: 1 unit at Rs. 1000, delivery fee Rs. 100 -> Total Rs. 1100
        var order = await CreateOrderFromCheckoutAsync(
            db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 1, OrderPaymentMethod.CashOnDelivery, "imm-idem-1");

        // Now modify catalog product, variant price, and delivery rule
        var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == seeded.ProductId);
        product.UpdateDetails("Export Luxury Wool Scarf (Renamed)", "New Description", product.Slug);
        var variant = product.Variants.Single(v => v.Id == seeded.FirstVariantId);
        variant.Update("SKU-MODIFIED", "Deluxe Gold", null, 3_500m, null, true);

        var rule = await db.DeliveryRules.SingleAsync(r => r.StoreId == seeded.StoreId);
        rule.Update(new DeliveryRuleSettings("Express Delivery", 0, DeliveryFeeType.Flat, 450m, null, "Same day", true, true,
            [new DeliveryZoneInput("Kathmandu", "KMC", "Baluwatar")]));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Query historical order via OrderQueryService
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var queryService = new OrderQueryService(db, accessor, authorizer);
        var detailResult = await queryService.GetOrderDetailAsync(order.Id);

        Assert.True(detailResult.IsSuccess);
        var detail = detailResult.Value!;

        // Invariant holds: Order snapshot values remain untouched
        Assert.Equal(1_000m, detail.MerchandiseSubtotalNpr);
        Assert.Equal(100m, detail.DeliveryFeeNpr);
        Assert.Equal(1_100m, detail.TotalNpr);
        Assert.Equal("Kathmandu Delivery", detail.DeliveryRuleName);

        var item = Assert.Single(detail.Items);
        Assert.Equal("Test Product", item.ProductTitle); // Original title
        Assert.Equal("Standard", item.VariantName); // Original variant
        Assert.Equal(1_000m, item.UnitPriceNpr); // Original price
        Assert.Equal(1_000m, item.LineTotalNpr);
    }

    // --- Helpers ---

    private static OrderOperationService CreateOrderOperationService(AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("m06-test"), authorizer);
        var options = Options.Create(new InventoryReservationOptions());
        var inventory = new InventoryService(db, accessor, authorizer, audit, clock, options);
        return new OrderOperationService(db, accessor, authorizer, inventory, audit, clock);
    }

    private static OrderCreationService CreateOrderCreationService(AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("order-create-test"), authorizer);
        var inventory = new InventoryService(db, accessor, authorizer, audit, clock, Options.Create(new InventoryReservationOptions()));
        return new OrderCreationService(db, accessor, inventory, audit, clock);
    }

    private static CheckoutSessionService CreateCheckoutService(AppDbContext db, TenantContextAccessor accessor, MutableTimeProvider clock, out StorefrontQuoteService quotes)
    {
        quotes = new StorefrontQuoteService(db, accessor, new StorefrontCatalogReadService(db), new StorefrontInventoryReadService(db), new EphemeralDataProtectionProvider(),
            Options.Create(new StorefrontQuoteOptions { LifetimeMinutes = 10 }), clock);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("checkout-test"), authorizer);
        var inventory = new InventoryService(db, accessor, authorizer, audit, clock, Options.Create(new InventoryReservationOptions()));
        return new CheckoutSessionService(db, accessor, quotes, new CustomerCheckoutService(db, accessor), inventory, audit, clock,
            Options.Create(new CheckoutSessionOptions { LifetimeMinutes = 10, PiiReviewDays = 30, ExpiryBatchSize = 100 }));
    }

    private static async Task<Order> CreateOrderFromCheckoutAsync(
        AppDbContext db,
        TenantContextAccessor accessor,
        MutableTimeProvider clock,
        string storeId,
        string variantId,
        int quantity,
        OrderPaymentMethod paymentMethod,
        string idempotencyKey)
    {
        var checkout = CreateCheckoutService(db, accessor, clock, out var quotes);
        var quote = await quotes.CreateQuoteAsync(new StorefrontQuoteRequest([new StorefrontQuoteLineRequest(variantId, quantity)], new StorefrontDestinationInput("NP", "Kathmandu", "KMC", "Baluwatar")));
        Assert.True(quote.IsSuccess);

        var sessionResult = await checkout.CreateAsync(new CreateCheckoutSessionRequest(quote.Value!.QuoteToken,
            new CheckoutCustomerInput("Ram Shrestha", "9812345678", "ram@example.com", false, true),
            new CheckoutAddressInput("Baluwatar Marg", null, "Kathmandu", "KMC", "Baluwatar", null), $"sess-{idempotencyKey}"));
        Assert.True(sessionResult.IsSuccess);

        var orderCreationService = CreateOrderCreationService(db, accessor, clock);
        var orderResult = await orderCreationService.CreateFromCheckoutAsync(new CreateOrderFromCheckoutRequest(sessionResult.Value!.Id, paymentMethod, idempotencyKey));
        Assert.True(orderResult.IsSuccess);

        return await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == orderResult.Value!.Id);
    }

    private static async Task<SeededStore> SeedStoreWithStockAsync(AppDbContext db, string tenantId, string slug, int openingStock, bool codAvailable)
    {
        var store = Store.Create(tenantId, new StoreSettings("Test Store", slug, null, StoreThemePreset.Default, null, "Kreyora", "test@example.com", null,
            null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));
        var product = Product.Create(tenantId, "Test Product", null, $"{slug}-prod");
        var variant = product.AddVariant("SKU-TEST", "Standard", null, 1_000m, null, true);
        product.Publish();

        var inventory = InventoryItem.Create(tenantId, variant.Id);
        inventory.ApplyMovement(openingStock);

        var movement = StockMovement.Create(
            tenantId,
            inventory.Id,
            variant.Id,
            StockMovementType.OpeningBalance,
            openingStock,
            "Initial stock",
            "01J00000000000000000000001",
            $"open-{variant.Id}",
            "fp-open",
            null,
            null,
            CommerceActorKind.Member);

        db.AddRange(store, product, inventory, movement,
            StoreProductPublication.Create(tenantId, store.Id, product.Id, StoreProductVisibility.Visible),
            DeliveryRule.Create(tenantId, store.Id, new DeliveryRuleSettings("Kathmandu Delivery", 0, DeliveryFeeType.Flat, 100m, null, "1-2 days", codAvailable, true,
                [new DeliveryZoneInput("Kathmandu", "KMC", "Baluwatar")])));
        await db.SaveChangesAsync();

        return new SeededStore(store.Id, product.Id, variant.Id);
    }

    private static async Task<Order> CreateDirectOrderAsync(AppDbContext db, string tenantId, OrderPaymentMethod paymentMethod, string customerName, string customerPhone)
    {
        var store = Store.Create(tenantId, new StoreSettings("Test Store", $"test-{Guid.NewGuid():N}"[..20], null, StoreThemePreset.Default, null,
            "Kreyora", "test@example.com", null, null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));
        var rule = DeliveryRule.Create(tenantId, store.Id, new DeliveryRuleSettings("Test Delivery", 0, DeliveryFeeType.Flat, 100m, null, "1-2 days", true, true,
            [new DeliveryZoneInput("Kathmandu", "KMC", "Baluwatar")]));
        db.AddRange(store, rule);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var session = CheckoutSession.Create(new CheckoutSessionCreation(
            tenantId, store.Id, null, new string('a', 64), now.AddMinutes(15), now.AddMinutes(15),
            customerName, customerPhone, "customer@example.com", "Baluwatar", null, "Kathmandu",
            "KMC", "Baluwatar", null, new string('b', 64), now.AddDays(30), 1500m, 0m, 100m,
            0m, 0m, 0m, 1600m, "NPR", rule.Id, rule.Name, "1-2 days", true, now));
        session.Complete(now);
        db.CheckoutSessions.Add(session);
        await db.SaveChangesAsync();

        var order = Order.Create(new OrderCreation(
            tenantId, store.Id, session.Id, null, paymentMethod,
            customerName, customerPhone, "customer@example.com", "Baluwatar", null, "Kathmandu",
            "KMC", "Baluwatar", null, 1500m, 0m, 100m, 0m, 0m, 0m, 1600m, "NPR", rule.Id, rule.Name, "1-2 days", true));

        var attempt = PaymentAttempt.Create(
            tenantId,
            order.Id,
            paymentMethod,
            order.TotalNpr,
            "NPR");

        db.Orders.Add(order);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<Tenant> CreateTenantAsync(AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static ServiceProvider BuildServices(
        AppDbContext db,
        TenantContextAccessor accessor,
        MutableTimeProvider clock,
        INotificationDeliveryProvider? deliveryProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor>(accessor);
        services.AddScoped(_ => db);
        services.AddSingleton<ITimeProvider>(clock);
        services.AddScoped<ITenantPermissionAuthorizer, TenantPermissionAuthorizer>();
        services.AddScoped<ICorrelationContext>(_ => new Correlation("notif-test"));
        services.AddSingleton(Options.Create(new NotificationOptions()));
        services.AddSingleton<INotificationTemplateRegistry, NotificationTemplateRegistry>();
        services.AddScoped<IAuditEventService, AuditEventService>();
        services.AddScoped<INotificationService, NotificationService>();

        if (deliveryProvider != null)
        {
            services.AddSingleton(deliveryProvider);
        }
        else
        {
            services.AddScoped<INotificationDeliveryProvider, DevelopmentNotificationProvider>();
            services.AddLogging();
        }

        return services.BuildServiceProvider();
    }

    private static TenantContext OwnerContext(string tenantId) => new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private sealed record SeededStore(string StoreId, string ProductId, string FirstVariantId);
    private sealed class Correlation(string correlationId) : ICorrelationContext { public string CorrelationId => correlationId; public void SetCorrelationId(string value) { } }
    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : ITimeProvider { public DateTimeOffset UtcNow { get; set; } = utcNow; }

    private sealed class FailingNotificationProvider : INotificationDeliveryProvider
    {
        public string ProviderName => "FailingProvider";
        public Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NotificationDeliveryResult(false, null, "Simulated network timeout connecting to provider."));
        }
    }
}
