using Kreyora.Domain.Orders;
using Kreyora.Domain.Tenancy;

namespace Kreyora.UnitTests.Domain;

public sealed class OrderTransitionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    private static Order CreateTestOrder(
        OrderPaymentMethod paymentMethod = OrderPaymentMethod.CashOnDelivery,
        bool codAvailable = true)
    {
        var creation = new OrderCreation(
            TenantId: "01M2F000000000000000000001",
            StoreId: "01M2F000000000000000000002",
            CheckoutSessionId: "01M2F000000000000000000003",
            CustomerId: "01M2F000000000000000000004",
            PaymentMethod: paymentMethod,
            CustomerName: "Ram Bahadur",
            CustomerPhone: "+9779800000000",
            CustomerEmail: "ram@example.com",
            AddressLine1: "Kathmandu 10",
            AddressLine2: null,
            District: "Kathmandu",
            Municipality: "Kathmandu Metro",
            Locality: "New Road",
            Landmark: "Near Peepal Bot",
            MerchandiseSubtotalNpr: 1000m,
            DiscountNpr: 0m,
            DeliveryFeeNpr: 100m,
            TaxNpr: 0m,
            ProviderFeeNpr: 0m,
            PlatformFeeNpr: 0m,
            TotalNpr: 1100m,
            Currency: "NPR",
            DeliveryRuleId: "01M2F000000000000000000005",
            DeliveryRuleName: "Standard Delivery",
            EstimatedEtaText: "1-2 days",
            CodAvailable: codAvailable);

        return Order.Create(creation);
    }

    [Theory]
    [InlineData(TenantRole.Viewer, OrderAction.Confirm)]
    [InlineData(TenantRole.Viewer, OrderAction.Cancel)]
    [InlineData(TenantRole.Viewer, OrderAction.Prepare)]
    [InlineData(TenantRole.Viewer, OrderAction.Dispatch)]
    [InlineData(TenantRole.Viewer, OrderAction.Deliver)]
    [InlineData(TenantRole.Viewer, OrderAction.MarkDeliveryFailed)]
    [InlineData(TenantRole.Viewer, OrderAction.VerifyPayment)]
    [InlineData(TenantRole.Viewer, OrderAction.RejectPayment)]
    [InlineData(TenantRole.Viewer, OrderAction.MarkCodCollected)]
    public void ViewerRole_IsDeniedForAllActions(TenantRole role, OrderAction action)
    {
        var order = CreateTestOrder();
        var allowed = OrderTransitionPolicy.CanExecute(order, action, role, out var denialReason);

        Assert.False(allowed);
        Assert.Equal(OrderActionDenialReasons.RoleNotAuthorized, denialReason);
    }

    [Theory]
    [InlineData(OrderAction.VerifyPayment)]
    [InlineData(OrderAction.RejectPayment)]
    [InlineData(OrderAction.MarkCodCollected)]
    public void OperatorRole_IsDeniedForPaymentManagementActions(OrderAction action)
    {
        var order = CreateTestOrder();
        var allowed = OrderTransitionPolicy.CanExecute(order, action, TenantRole.Operator, out var denialReason);

        Assert.False(allowed);
        Assert.Equal(OrderActionDenialReasons.RoleNotAuthorized, denialReason);
    }

    [Fact]
    public void OperatorRole_IsAllowedForFulfillmentAndOrderActions_WhenStateAllows()
    {
        var order = CreateTestOrder(); // PendingConfirmation
        Assert.True(OrderTransitionPolicy.CanExecute(order, OrderAction.Confirm, TenantRole.Operator, out _));
        Assert.True(OrderTransitionPolicy.CanExecute(order, OrderAction.Cancel, TenantRole.Operator, out _));

        order.Confirm(Now); // Confirmed, Unfulfilled
        Assert.True(OrderTransitionPolicy.CanExecute(order, OrderAction.Prepare, TenantRole.Operator, out _));

        order.Prepare(Now); // Processing, Ready
        Assert.True(OrderTransitionPolicy.CanExecute(order, OrderAction.Dispatch, TenantRole.Operator, out _));

        order.Dispatch(Now); // Processing, Dispatched
        Assert.True(OrderTransitionPolicy.CanExecute(order, OrderAction.Deliver, TenantRole.Operator, out _));
        Assert.True(OrderTransitionPolicy.CanExecute(order, OrderAction.MarkDeliveryFailed, TenantRole.Operator, out _));
    }

    [Fact]
    public void CancelledOrder_DeniesAllActions()
    {
        var order = CreateTestOrder();
        order.Cancel("Customer changed mind", Now);

        foreach (var action in Enum.GetValues<OrderAction>())
        {
            var allowed = OrderTransitionPolicy.CanExecute(order, action, TenantRole.Owner, out var denialReason);
            Assert.False(allowed, $"Action {action} should be denied on cancelled order");
            Assert.Equal(OrderActionDenialReasons.OrderAlreadyCancelled, denialReason);
        }
    }

    [Fact]
    public void FulfilledOrder_DeniesCancellationAndFulfillmentActions()
    {
        var order = CreateTestOrder();
        order.Confirm(Now);
        order.Prepare(Now);
        order.Dispatch(Now);
        order.Deliver(Now); // Fulfilled, Delivered

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(FulfilmentStatus.Delivered, order.FulfilmentStatus);

        // Cancel denied
        Assert.False(OrderTransitionPolicy.CanExecute(order, OrderAction.Cancel, TenantRole.Owner, out var cancelReason));
        Assert.Equal(OrderActionDenialReasons.OrderAlreadyFulfilled, cancelReason);

        // Fulfilment actions denied
        foreach (var action in new[] { OrderAction.Prepare, OrderAction.Dispatch, OrderAction.Deliver, OrderAction.MarkDeliveryFailed })
        {
            Assert.False(OrderTransitionPolicy.CanExecute(order, action, TenantRole.Owner, out var reason));
            Assert.Equal(OrderActionDenialReasons.OrderAlreadyFulfilled, reason);
        }
    }

    [Fact]
    public void DispatchedOrder_CannotBeCancelledDirectly()
    {
        var order = CreateTestOrder();
        order.Confirm(Now);
        order.Prepare(Now);
        order.Dispatch(Now);

        Assert.False(OrderTransitionPolicy.CanExecute(order, OrderAction.Cancel, TenantRole.Owner, out var reason));
        Assert.Equal(OrderActionDenialReasons.DispatchedOrderCannotCancelDirectly, reason);
    }

    [Fact]
    public void MerchantQrOrder_CannotBeDispatchedUntilPaid()
    {
        var order = CreateTestOrder(paymentMethod: OrderPaymentMethod.MerchantQr, codAvailable: false);
        order.Confirm(Now);
        order.Prepare(Now); // Ready, but PaymentStatus is AwaitingVerification

        Assert.Equal(PaymentStatus.AwaitingVerification, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Ready, order.FulfilmentStatus);

        // Dispatch denied because payment unverified
        Assert.False(OrderTransitionPolicy.CanExecute(order, OrderAction.Dispatch, TenantRole.Owner, out var reason));
        Assert.Equal(OrderActionDenialReasons.MerchantQrPaymentUnverified, reason);

        // Verify payment
        order.VerifyPayment(Now);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);

        // Dispatch now allowed
        Assert.True(OrderTransitionPolicy.CanExecute(order, OrderAction.Dispatch, TenantRole.Owner, out _));
    }

    [Fact]
    public void CodOrder_CanBeDispatchedWhilePaymentPending()
    {
        var order = CreateTestOrder(paymentMethod: OrderPaymentMethod.CashOnDelivery, codAvailable: true);
        order.Confirm(Now);
        order.Prepare(Now);

        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Ready, order.FulfilmentStatus);

        // Dispatch allowed for COD with payment pending
        Assert.True(OrderTransitionPolicy.CanExecute(order, OrderAction.Dispatch, TenantRole.Owner, out _));
    }

    [Fact]
    public void CodCollection_RequiresDispatchOrDelivery()
    {
        var order = CreateTestOrder(paymentMethod: OrderPaymentMethod.CashOnDelivery, codAvailable: true);

        // Before dispatch: denied
        Assert.False(OrderTransitionPolicy.CanExecute(order, OrderAction.MarkCodCollected, TenantRole.Owner, out var reasonBefore));
        Assert.Equal(OrderActionDenialReasons.CodNotReadyForCollection, reasonBefore);

        order.Confirm(Now);
        order.Prepare(Now);
        Assert.False(OrderTransitionPolicy.CanExecute(order, OrderAction.MarkCodCollected, TenantRole.Owner, out _));

        // After dispatch: allowed
        order.Dispatch(Now);
        Assert.True(OrderTransitionPolicy.CanExecute(order, OrderAction.MarkCodCollected, TenantRole.Owner, out _));

        // After delivery: allowed
        order.Deliver(Now);
        Assert.True(OrderTransitionPolicy.CanExecute(order, OrderAction.MarkCodCollected, TenantRole.Owner, out _));
    }

    [Fact]
    public void PaymentVerification_OnlyApplicableToMerchantQr()
    {
        var codOrder = CreateTestOrder(paymentMethod: OrderPaymentMethod.CashOnDelivery);

        Assert.False(OrderTransitionPolicy.CanExecute(codOrder, OrderAction.VerifyPayment, TenantRole.Owner, out var verifyReason));
        Assert.Equal(OrderActionDenialReasons.PaymentMethodNotMerchantQr, verifyReason);

        Assert.False(OrderTransitionPolicy.CanExecute(codOrder, OrderAction.RejectPayment, TenantRole.Owner, out var rejectReason));
        Assert.Equal(OrderActionDenialReasons.PaymentMethodNotMerchantQr, rejectReason);
    }

    [Fact]
    public void CodCollection_OnlyApplicableToCod()
    {
        var qrOrder = CreateTestOrder(paymentMethod: OrderPaymentMethod.MerchantQr, codAvailable: false);

        Assert.False(OrderTransitionPolicy.CanExecute(qrOrder, OrderAction.MarkCodCollected, TenantRole.Owner, out var reason));
        Assert.Equal(OrderActionDenialReasons.PaymentMethodNotCod, reason);
    }

    [Theory]
    [InlineData(OrderAction.Cancel, true)]
    [InlineData(OrderAction.RejectPayment, true)]
    [InlineData(OrderAction.MarkDeliveryFailed, true)]
    [InlineData(OrderAction.Confirm, false)]
    [InlineData(OrderAction.Prepare, false)]
    [InlineData(OrderAction.Dispatch, false)]
    [InlineData(OrderAction.Deliver, false)]
    [InlineData(OrderAction.VerifyPayment, false)]
    [InlineData(OrderAction.MarkCodCollected, false)]
    public void RequiresReason_And_IsDestructive_MatchExpectations(OrderAction action, bool expected)
    {
        Assert.Equal(expected, OrderTransitionPolicy.RequiresReason(action));
        Assert.Equal(expected, OrderTransitionPolicy.IsDestructive(action));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("ab", false)] // < 3 characters
    [InlineData("abc", true)]  // 3 characters valid
    [InlineData("Valid reason for cancellation", true)]
    public void ValidateReason_EnforcesLengthConstraints(string? reason, bool expectedValid)
    {
        var valid = OrderTransitionPolicy.ValidateReason(OrderAction.Cancel, reason, out var error);
        Assert.Equal(expectedValid, valid);
        if (!expectedValid)
        {
            Assert.Equal(OrderActionDenialReasons.ReasonRequired, error);
        }
        else
        {
            Assert.Null(error);
        }
    }

    [Fact]
    public void ValidateReason_RejectsReasonsExceeding500Chars()
    {
        var longReason = new string('x', 501);
        var valid = OrderTransitionPolicy.ValidateReason(OrderAction.Cancel, longReason, out var error);
        Assert.False(valid);
        Assert.Equal(OrderActionDenialReasons.ReasonRequired, error);
    }

    [Fact]
    public void GetAllowedActions_ReturnsAllEvaluationsMatchingCanExecute()
    {
        var order = CreateTestOrder();
        var evaluations = OrderTransitionPolicy.GetAllowedActions(order, TenantRole.Owner);

        Assert.Equal(Enum.GetValues<OrderAction>().Length, evaluations.Count);
        foreach (var eval in evaluations)
        {
            var expectedAllowed = OrderTransitionPolicy.CanExecute(order, eval.Action, TenantRole.Owner, out var expectedReason);
            Assert.Equal(expectedAllowed, eval.IsAllowed);
            Assert.Equal(expectedReason, eval.DenialReason);
            Assert.Equal(OrderTransitionPolicy.RequiresReason(eval.Action), eval.RequiresReason);
            Assert.Equal(OrderTransitionPolicy.IsDestructive(eval.Action), eval.IsDestructive);
        }
    }
}

