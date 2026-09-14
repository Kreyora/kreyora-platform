using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Inventory;
using Kreyora.Application.Orders;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Common;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Payments;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Catalog;
using Kreyora.Infrastructure.Customers;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Orders;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Storefront;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Orders;

public sealed class OrderFulfilmentInventoryCoordinationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public OrderFulfilmentInventoryCoordinationTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task CodOrder_FullDeliveryLifecycle_PreservesCommittedStockAndReconciles()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "cod-full");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "cod-full-store", 10, true);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 3, OrderPaymentMethod.CashOnDelivery, "cod-full-create");

        // Verify committed stock
        var balanceAfterCreate = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(7, balanceAfterCreate.OnHandQuantity);
        Assert.Equal(0, balanceAfterCreate.ReservedQuantity);

        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        // 1. Confirm
        var confirmResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, version, "key-confirm"));
        Assert.True(confirmResult.IsSuccess);
        Assert.Equal(OrderStatus.Confirmed, confirmResult.Value!.Status);
        version = confirmResult.Value.RowVersion;

        // 2. Prepare (Mark Ready)
        var prepareResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Prepare, null, version, "key-prepare"));
        Assert.True(prepareResult.IsSuccess);
        Assert.Equal(FulfilmentStatus.Ready, prepareResult.Value!.FulfilmentStatus);
        version = prepareResult.Value.RowVersion;

        // 3. Dispatch
        var dispatchResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Dispatch, null, version, "key-dispatch"));
        Assert.True(dispatchResult.IsSuccess);
        Assert.Equal(FulfilmentStatus.Dispatched, dispatchResult.Value!.FulfilmentStatus);
        version = dispatchResult.Value.RowVersion;

        // 4. Deliver
        var deliverResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Deliver, null, version, "key-deliver"));
        Assert.True(deliverResult.IsSuccess);
        Assert.Equal(FulfilmentStatus.Delivered, deliverResult.Value!.FulfilmentStatus);
        Assert.Equal(OrderStatus.Fulfilled, deliverResult.Value.Status);
        version = deliverResult.Value.RowVersion;

        // 5. Mark COD Collected
        var collectResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.MarkCodCollected, null, version, "key-collected"));
        Assert.True(collectResult.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, collectResult.Value!.PaymentStatus);

        // Verify stock reconciliation
        var reconciliation = await inventoryService.ReconcileInventoryAsync(seeded.FirstVariantId);
        Assert.True(reconciliation.IsSuccess);
        Assert.True(reconciliation.Value!.IsMatch);
        Assert.Equal(7, reconciliation.Value.MaterializedOnHandQuantity);
        Assert.Equal(7, reconciliation.Value.LedgerOnHandQuantity);

        // Verify outbox messages were produced
        var outboxEvents = await db.OutboxMessages.Where(m => m.TenantId == tenant.Id).OrderBy(m => m.CreatedAt).ToListAsync();
        Assert.Contains(outboxEvents, m => m.Type == "order.confirmed.v1");
        Assert.Contains(outboxEvents, m => m.Type == "order.prepared.v1");
        Assert.Contains(outboxEvents, m => m.Type == "order.dispatched.v1");
        Assert.Contains(outboxEvents, m => m.Type == "order.delivered.v1");
        Assert.Contains(outboxEvents, m => m.Type == "payment.cod_collected.v1");
    }

    [Fact]
    public async Task MerchantQrOrder_FullDeliveryLifecycle_BlocksDispatchUntilVerifiedAndReconciles()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "qr-full");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "qr-full-store", 10, false);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 2, OrderPaymentMethod.MerchantQr, "qr-full-create");

        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        // 1. Confirm
        var confirmResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, version, "key-confirm"));
        Assert.True(confirmResult.IsSuccess);
        version = confirmResult.Value!.RowVersion;

        // 2. Prepare (Mark Ready)
        var prepareResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Prepare, null, version, "key-prepare"));
        Assert.True(prepareResult.IsSuccess);
        version = prepareResult.Value!.RowVersion;

        // 3. Attempt Dispatch before payment verified -> BLOCKED by policy
        var prematureDispatch = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Dispatch, null, version, "key-dispatch-premature"));
        Assert.True(prematureDispatch.IsFailure);
        Assert.Equal(OrderActionDenialReasons.MerchantQrPaymentUnverified, prematureDispatch.Error!.Detail);

        // 4. Verify Payment
        var paymentAttempt = await db.PaymentAttempts.SingleAsync(pa => pa.OrderId == order.Id);
        var verifyResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.VerifyPayment, null, version, "key-verify", paymentAttempt.Id, "FONEPAY-TRX-12345"));
        Assert.True(verifyResult.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, verifyResult.Value!.PaymentStatus);
        version = verifyResult.Value.RowVersion;

        // 5. Dispatch now succeeds!
        var dispatchResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Dispatch, null, version, "key-dispatch"));
        Assert.True(dispatchResult.IsSuccess);
        Assert.Equal(FulfilmentStatus.Dispatched, dispatchResult.Value!.FulfilmentStatus);
        version = dispatchResult.Value.RowVersion;

        // 6. Deliver
        var deliverResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Deliver, null, version, "key-deliver"));
        Assert.True(deliverResult.IsSuccess);
        Assert.Equal(FulfilmentStatus.Delivered, deliverResult.Value!.FulfilmentStatus);
        Assert.Equal(OrderStatus.Fulfilled, deliverResult.Value.Status);

        // Stock reconciliation
        var reconciliation = await inventoryService.ReconcileInventoryAsync(seeded.FirstVariantId);
        Assert.True(reconciliation.IsSuccess);
        Assert.True(reconciliation.Value!.IsMatch);
        Assert.Equal(8, reconciliation.Value.MaterializedOnHandQuantity);
        Assert.Equal(8, reconciliation.Value.LedgerOnHandQuantity);
    }

    [Fact]
    public async Task OrderCancellation_FromPendingConfirmation_RestocksCommittedInventoryAndReconciles()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "cancel-pending");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "cancel-pending-store", 10, true);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 4, OrderPaymentMethod.CashOnDelivery, "cancel-pending-create");

        // Stock after order creation is 6
        var balanceBefore = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(6, balanceBefore.OnHandQuantity);

        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        // Cancel order
        var cancelResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Customer changed their mind before confirmation", version, "key-cancel"));
        Assert.True(cancelResult.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, cancelResult.Value!.Status);
        Assert.Equal(FulfilmentStatus.Cancelled, cancelResult.Value.FulfilmentStatus);

        // Stock MUST be restocked to 10
        db.ChangeTracker.Clear();
        var balanceAfter = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(10, balanceAfter.OnHandQuantity);
        Assert.Equal(0, balanceAfter.ReservedQuantity);

        // Verify restock movement exists
        var restockMovement = await db.StockMovements.SingleAsync(m => m.Type == StockMovementType.OrderRestock && m.ReferenceId == order.Id);
        Assert.Equal(4, restockMovement.QuantityDelta);

        // Verify payment attempt was expired
        var paymentAttempt = await db.PaymentAttempts.SingleAsync(pa => pa.OrderId == order.Id);
        Assert.Equal(PaymentAttemptStatus.Expired, paymentAttempt.Status);

        // Reconcile
        var reconciliation = await inventoryService.ReconcileInventoryAsync(seeded.FirstVariantId);
        Assert.True(reconciliation.IsSuccess);
        Assert.True(reconciliation.Value!.IsMatch);
        Assert.Equal(10, reconciliation.Value.MaterializedOnHandQuantity);
        Assert.Equal(10, reconciliation.Value.LedgerOnHandQuantity);

        // Outbox event
        var cancelOutbox = await db.OutboxMessages.SingleAsync(m => m.TenantId == tenant.Id && m.Type == "order.cancelled.v1");
        Assert.Contains(order.Id, cancelOutbox.Content);
    }

    [Fact]
    public async Task OrderCancellation_FromConfirmed_RestocksCommittedInventory()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "cancel-conf");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "cancel-conf-store", 10, true);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 3, OrderPaymentMethod.CashOnDelivery, "cancel-conf-create");

        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        var confirmResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, version, "key-confirm"));
        Assert.True(confirmResult.IsSuccess);
        version = confirmResult.Value!.RowVersion;

        var cancelResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Item damaged during staging check", version, "key-cancel"));
        Assert.True(cancelResult.IsSuccess);

        db.ChangeTracker.Clear();
        var balance = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(10, balance.OnHandQuantity);

        var reconciliation = await inventoryService.ReconcileInventoryAsync(seeded.FirstVariantId);
        Assert.True(reconciliation.IsSuccess);
        Assert.True(reconciliation.Value!.IsMatch);
    }

    [Fact]
    public async Task OrderCancellation_FromReady_RestocksCommittedInventory()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "cancel-ready");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "cancel-ready-store", 10, true);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 2, OrderPaymentMethod.CashOnDelivery, "cancel-ready-create");

        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        var confirm = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, version, "key-confirm"));
        version = confirm.Value!.RowVersion;

        var prepare = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Prepare, null, version, "key-prepare"));
        version = prepare.Value!.RowVersion;
        Assert.Equal(FulfilmentStatus.Ready, prepare.Value.FulfilmentStatus);

        var cancel = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Customer cancelled before courier arrived", version, "key-cancel"));
        Assert.True(cancel.IsSuccess);

        db.ChangeTracker.Clear();
        var balance = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(10, balance.OnHandQuantity);

        var reconciliation = await inventoryService.ReconcileInventoryAsync(seeded.FirstVariantId);
        Assert.True(reconciliation.IsSuccess);
        Assert.True(reconciliation.Value!.IsMatch);
    }

    [Fact]
    public async Task OrderCancellation_AfterDeliveryFailed_RestocksCommittedInventory()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "cancel-fail");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "cancel-fail-store", 10, true);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 2, OrderPaymentMethod.CashOnDelivery, "cancel-fail-create");

        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        version = (await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, version, "key-confirm"))).Value!.RowVersion;
        version = (await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Prepare, null, version, "key-prepare"))).Value!.RowVersion;
        version = (await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Dispatch, null, version, "key-dispatch"))).Value!.RowVersion;

        // Dispatched order CANNOT cancel directly
        var directCancel = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Attempt direct cancel while dispatched", version, "key-cancel-premature"));
        Assert.True(directCancel.IsFailure);
        Assert.Equal(OrderActionDenialReasons.DispatchedOrderCannotCancelDirectly, directCancel.Error!.Detail);

        // Mark delivery failed
        var failResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.MarkDeliveryFailed, "Recipient unreachable at address after 3 attempts", version, "key-failed"));
        Assert.True(failResult.IsSuccess);
        Assert.Equal(FulfilmentStatus.Failed, failResult.Value!.FulfilmentStatus);
        version = failResult.Value.RowVersion;

        // Cancel order now that goods are returned from delivery
        var cancelResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Package returned to warehouse and restocked", version, "key-cancel"));
        Assert.True(cancelResult.IsSuccess);

        db.ChangeTracker.Clear();
        var balance = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(10, balance.OnHandQuantity);

        var reconciliation = await inventoryService.ReconcileInventoryAsync(seeded.FirstVariantId);
        Assert.True(reconciliation.IsSuccess);
        Assert.True(reconciliation.Value!.IsMatch);
    }

    [Fact]
    public async Task DeliveryFailed_RetryPreparationAndDelivery_KeepsStockCommitted()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "retry-deliv");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "retry-deliv-store", 10, true);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 2, OrderPaymentMethod.CashOnDelivery, "retry-deliv-create");

        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        version = (await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, version, "key-confirm"))).Value!.RowVersion;
        version = (await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Prepare, null, version, "key-prepare"))).Value!.RowVersion;
        version = (await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Dispatch, null, version, "key-dispatch"))).Value!.RowVersion;
        version = (await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.MarkDeliveryFailed, "Recipient phone switched off", version, "key-failed"))).Value!.RowVersion;

        // Re-prepare (Prepare allowed from Failed status per policy!)
        var rePrepare = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Prepare, null, version, "key-reprepare"));
        Assert.True(rePrepare.IsSuccess);
        Assert.Equal(FulfilmentStatus.Ready, rePrepare.Value!.FulfilmentStatus);
        version = rePrepare.Value.RowVersion;

        // Re-dispatch
        var reDispatch = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Dispatch, null, version, "key-redispatch"));
        Assert.True(reDispatch.IsSuccess);
        Assert.Equal(FulfilmentStatus.Dispatched, reDispatch.Value!.FulfilmentStatus);
        version = reDispatch.Value.RowVersion;

        // Deliver
        var deliver = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Deliver, null, version, "key-deliver"));
        Assert.True(deliver.IsSuccess);
        Assert.Equal(FulfilmentStatus.Delivered, deliver.Value!.FulfilmentStatus);

        // Stock was never restocked: remains 8
        var reconciliation = await inventoryService.ReconcileInventoryAsync(seeded.FirstVariantId);
        Assert.True(reconciliation.IsSuccess);
        Assert.True(reconciliation.Value!.IsMatch);
        Assert.Equal(8, reconciliation.Value.MaterializedOnHandQuantity);
        Assert.Equal(8, reconciliation.Value.LedgerOnHandQuantity);
    }

    [Fact]
    public async Task MultiItemOrder_Cancellation_RestocksAllVariants()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "multi-item");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        // Seed store with two products
        var store = Store.Create(tenant.Id, new StoreSettings("Multi Store", "multi-store", null, StoreThemePreset.Default, null, "Kreyora", "multi@example.com", null,
            null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));
        var product1 = Product.Create(tenant.Id, "Product 1", null, "prod-1");
        var variant1 = product1.AddVariant("SKU-1", "V1", null, 500m, null, true);
        product1.Publish();
        var product2 = Product.Create(tenant.Id, "Product 2", null, "prod-2");
        var variant2 = product2.AddVariant("SKU-2", "V2", null, 800m, null, true);
        product2.Publish();

        var inv1 = InventoryItem.Create(tenant.Id, variant1.Id);
        inv1.ApplyMovement(10);
        var inv2 = InventoryItem.Create(tenant.Id, variant2.Id);
        inv2.ApplyMovement(10);

        var mov1 = StockMovement.Create(tenant.Id, inv1.Id, variant1.Id, StockMovementType.OpeningBalance, 10, "Opening", "01J00000000000000000000001", $"open-{variant1.Id}", "fp", null, null, CommerceActorKind.Member);
        var mov2 = StockMovement.Create(tenant.Id, inv2.Id, variant2.Id, StockMovementType.OpeningBalance, 10, "Opening", "01J00000000000000000000001", $"open-{variant2.Id}", "fp", null, null, CommerceActorKind.Member);

        db.AddRange(store, product1, product2, inv1, inv2, mov1, mov2,
            StoreProductPublication.Create(tenant.Id, store.Id, product1.Id, StoreProductVisibility.Visible),
            StoreProductPublication.Create(tenant.Id, store.Id, product2.Id, StoreProductVisibility.Visible),
            DeliveryRule.Create(tenant.Id, store.Id, new DeliveryRuleSettings("Kathmandu", 0, DeliveryFeeType.Flat, 100m, null, "1-2 days", true, true,
                [new DeliveryZoneInput("Kathmandu", "KMC", "Baluwatar")])));
        await db.SaveChangesAsync();

        // Checkout session with both variants
        var checkout = CreateCheckoutService(db, accessor, clock, out var quotes);
        var quote = await quotes.CreateQuoteAsync(new StorefrontQuoteRequest([
            new StorefrontQuoteLineRequest(variant1.Id, 2),
            new StorefrontQuoteLineRequest(variant2.Id, 3)
        ], new StorefrontDestinationInput("NP", "Kathmandu", "KMC", "Baluwatar")));
        Assert.True(quote.IsSuccess);

        var sessionResult = await checkout.CreateAsync(new CreateCheckoutSessionRequest(quote.Value!.QuoteToken,
            new CheckoutCustomerInput("Multi Customer", "9812345678", "multi@example.com", false, true),
            new CheckoutAddressInput("Baluwatar Road", null, "Kathmandu", "KMC", "Baluwatar", null), "multi-sess-key"));
        Assert.True(sessionResult.IsSuccess);

        var orderCreationService = CreateOrderCreationService(db, accessor, clock);
        var orderResult = await orderCreationService.CreateFromCheckoutAsync(new CreateOrderFromCheckoutRequest(sessionResult.Value!.Id, OrderPaymentMethod.CashOnDelivery, "multi-order-key"));
        Assert.True(orderResult.IsSuccess);

        // Balances after order creation: 10-2=8 and 10-3=7
        db.ChangeTracker.Clear();
        var b1 = await db.InventoryItems.SingleAsync(i => i.VariantId == variant1.Id);
        var b2 = await db.InventoryItems.SingleAsync(i => i.VariantId == variant2.Id);
        Assert.Equal(8, b1.OnHandQuantity);
        Assert.Equal(7, b2.OnHandQuantity);

        // Cancel order
        var order = await db.Orders.SingleAsync(o => o.Id == orderResult.Value!.Id);
        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        var cancel = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Cancel, "Customer cancelled entire multi-item order", version, "multi-cancel-key"));
        Assert.True(cancel.IsSuccess);

        // Balances restocked to 10 and 10!
        db.ChangeTracker.Clear();
        var b1After = await db.InventoryItems.SingleAsync(i => i.VariantId == variant1.Id);
        var b2After = await db.InventoryItems.SingleAsync(i => i.VariantId == variant2.Id);
        Assert.Equal(10, b1After.OnHandQuantity);
        Assert.Equal(10, b2After.OnHandQuantity);

        // Reconcile both
        var rec1 = await inventoryService.ReconcileInventoryAsync(variant1.Id);
        var rec2 = await inventoryService.ReconcileInventoryAsync(variant2.Id);
        Assert.True(rec1.Value!.IsMatch);
        Assert.True(rec2.Value!.IsMatch);
    }

    [Fact]
    public async Task MerchantQrOrder_PaymentRejected_ThenCancelled_RestocksStock()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "qr-reject");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "qr-reject-store", 10, false);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 2, OrderPaymentMethod.MerchantQr, "qr-reject-create");

        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        // Reject payment
        var paymentAttempt = await db.PaymentAttempts.SingleAsync(pa => pa.OrderId == order.Id);
        var rejectResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.RejectPayment, "Payment slip was fraudulent or unreadable", version, "key-reject", paymentAttempt.Id));
        Assert.True(rejectResult.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, rejectResult.Value!.PaymentStatus);
        version = rejectResult.Value.RowVersion;

        // Cancel order
        var cancelResult = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Customer failed to provide valid payment", version, "key-cancel"));
        Assert.True(cancelResult.IsSuccess);

        // Restocked
        db.ChangeTracker.Clear();
        var balance = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(10, balance.OnHandQuantity);

        var paAfter = await db.PaymentAttempts.SingleAsync(pa => pa.Id == paymentAttempt.Id);
        Assert.Equal(PaymentAttemptStatus.Rejected, paAfter.Status);

        var rec = await inventoryService.ReconcileInventoryAsync(seeded.FirstVariantId);
        Assert.True(rec.Value!.IsMatch);
    }

    [Fact]
    public async Task ConcurrentCancelAndDispatch_ResolvedSafelyByConcurrencyToken()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "race-cancel-dispatch");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "race-store", 10, true);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 2, OrderPaymentMethod.CashOnDelivery, "race-order-create");

        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        version = (await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Confirm, null, version, "race-confirm"))).Value!.RowVersion;
        version = (await service.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Prepare, null, version, "race-prepare"))).Value!.RowVersion;

        // Order is now in Ready status with version V.
        // Race: Task 1 attempts Dispatch, Task 2 attempts Cancel with the SAME initial version V!
        var initialVersion = version;

        var task1 = Task.Run(async () =>
        {
            var acc = new TenantContextAccessor();
            await using var db1 = fixture.CreateDbContext(acc);
            using var s1 = acc.BeginScope(OwnerContext(tenant.Id));
            var s = CreateOrderOperationService(db1, acc, clock, out _);
            return await s.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Dispatch, null, initialVersion, "race-task-dispatch"));
        });

        var task2 = Task.Run(async () =>
        {
            var acc = new TenantContextAccessor();
            await using var db2 = fixture.CreateDbContext(acc);
            using var s2 = acc.BeginScope(OwnerContext(tenant.Id));
            var s = CreateOrderOperationService(db2, acc, clock, out _);
            return await s.ExecuteActionAsync(new ExecuteOrderActionRequest(order.Id, OrderAction.Cancel, "Race cancel request", initialVersion, "race-task-cancel"));
        });

        var results = await Task.WhenAll(task1, task2);
        var dispatchOutcome = results[0];
        var cancelOutcome = results[1];

        // Exactly one should succeed, or if one won, the other must have encountered a conflict or error
        var successes = results.Count(r => r.IsSuccess);
        Assert.Equal(1, successes);

        db.ChangeTracker.Clear();
        var finalOrder = await db.Orders.SingleAsync(o => o.Id == order.Id);
        var rec = await inventoryService.ReconcileInventoryAsync(seeded.FirstVariantId);
        Assert.True(rec.Value!.IsMatch);

        if (dispatchOutcome.IsSuccess)
        {
            // Dispatch won: order is Dispatched, stock was NOT restocked (on-hand is 8)
            Assert.Equal(FulfilmentStatus.Dispatched, finalOrder.FulfilmentStatus);
            Assert.Equal(8, rec.Value.MaterializedOnHandQuantity);
        }
        else
        {
            // Cancel won: order is Cancelled, stock WAS restocked (on-hand is 10)
            Assert.Equal(OrderStatus.Cancelled, finalOrder.Status);
            Assert.Equal(10, rec.Value.MaterializedOnHandQuantity);
        }
    }

    [Fact]
    public async Task DuplicateCancelCommand_IsIdempotentAndDoesNotDoubleRestock()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "dup-cancel");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var seeded = await SeedStoreWithStockAsync(db, tenant.Id, "dup-cancel-store", 10, true);
        var order = await CreateOrderFromCheckoutAsync(db, accessor, clock, seeded.StoreId, seeded.FirstVariantId, 3, OrderPaymentMethod.CashOnDelivery, "dup-cancel-create");

        var service = CreateOrderOperationService(db, accessor, clock, out var inventoryService);
        var version = db.Entry(order).Property<uint>("xmin").CurrentValue;

        // First cancellation
        var firstCancel = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Customer duplicate cancel test", version, "idemp-cancel-key-999"));
        Assert.True(firstCancel.IsSuccess);
        Assert.False(firstCancel.Value!.WasReplayed);

        // Immediate replay of same command with same idempotency key
        var secondCancel = await service.ExecuteActionAsync(new ExecuteOrderActionRequest(
            order.Id, OrderAction.Cancel, "Customer duplicate cancel test", version, "idemp-cancel-key-999"));
        Assert.True(secondCancel.IsSuccess);
        Assert.True(secondCancel.Value!.WasReplayed);

        // Stock MUST be 10, NOT 13!
        db.ChangeTracker.Clear();
        var balance = await db.InventoryItems.SingleAsync(i => i.VariantId == seeded.FirstVariantId);
        Assert.Equal(10, balance.OnHandQuantity);

        // Exactly one restock movement in database
        var movements = await db.StockMovements.Where(m => m.Type == StockMovementType.OrderRestock && m.ReferenceId == order.Id).ToListAsync();
        Assert.Single(movements);

        // Reconcile
        var reconciliation = await inventoryService.ReconcileInventoryAsync(seeded.FirstVariantId);
        Assert.True(reconciliation.IsSuccess);
        Assert.True(reconciliation.Value!.IsMatch);
        Assert.Equal(10, reconciliation.Value.MaterializedOnHandQuantity);
    }

    // --- Helpers ---

    private static OrderOperationService CreateOrderOperationService(
        AppDbContext db,
        TenantContextAccessor accessor,
        MutableTimeProvider clock,
        out InventoryService inventoryService)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("coord-test"), authorizer);
        var options = Options.Create(new InventoryReservationOptions());
        inventoryService = new InventoryService(db, accessor, authorizer, audit, clock, options);
        return new OrderOperationService(db, accessor, authorizer, inventoryService, audit, clock);
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

    private static async Task<Tenant> CreateTenantAsync(AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static TenantContext OwnerContext(string tenantId) => new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private sealed record SeededStore(string StoreId, string ProductId, string FirstVariantId);
    private sealed class Correlation(string correlationId) : ICorrelationContext { public string CorrelationId => correlationId; public void SetCorrelationId(string value) { } }
    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : Kreyora.Domain.Abstractions.ITimeProvider { public DateTimeOffset UtcNow { get; set; } = utcNow; }
}

