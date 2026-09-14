using Kreyora.Domain.Orders;

namespace Kreyora.UnitTests.Domain;

public sealed class OrderAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    private static Order CreateOrder(
        OrderPaymentMethod paymentMethod = OrderPaymentMethod.CashOnDelivery,
        bool codAvailable = true)
    {
        var creation = new OrderCreation(
            TenantId: "01M2F000000000000000000001",
            StoreId: "01M2F000000000000000000002",
            CheckoutSessionId: "01M2F000000000000000000003",
            CustomerId: "01M2F000000000000000000004",
            PaymentMethod: paymentMethod,
            CustomerName: "Sita Sharma",
            CustomerPhone: "+9779811111111",
            CustomerEmail: "sita@example.com",
            AddressLine1: "Pokhara Lakeside",
            AddressLine2: null,
            District: "Kaski",
            Municipality: "Pokhara Metro",
            Locality: "Lakeside",
            Landmark: "Near Hallan Chowk",
            MerchandiseSubtotalNpr: 2000m,
            DiscountNpr: 0m,
            DeliveryFeeNpr: 150m,
            TaxNpr: 0m,
            ProviderFeeNpr: 0m,
            PlatformFeeNpr: 0m,
            TotalNpr: 2150m,
            Currency: "NPR",
            DeliveryRuleId: "01M2F000000000000000000005",
            DeliveryRuleName: "Pokhara Flat",
            EstimatedEtaText: "2-3 days",
            CodAvailable: codAvailable);

        return Order.Create(creation);
    }

    [Fact]
    public void Confirm_TransitionsPendingConfirmationToConfirmed()
    {
        var order = CreateOrder();
        Assert.Equal(OrderStatus.PendingConfirmation, order.Status);

        order.Confirm(Now);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(Now, order.ModifiedAt);

        // Cannot confirm again
        var ex = Assert.Throws<InvalidOperationException>(() => order.Confirm(Now.AddMinutes(1)));
        Assert.Equal(OrderActionDenialReasons.OrderNotPendingConfirmation, ex.Message);
    }

    [Fact]
    public void Cancel_TransitionsToCancelled_AndRequiresValidReason()
    {
        var order = CreateOrder();

        // Missing or short reason throws ArgumentException
        Assert.Throws<ArgumentException>(() => order.Cancel("", Now));
        Assert.Throws<ArgumentException>(() => order.Cancel("ab", Now));

        order.Cancel("Customer requested cancellation due to travel", Now);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(FulfilmentStatus.Cancelled, order.FulfilmentStatus);
        Assert.Equal(Now, order.ModifiedAt);

        // Cannot cancel already cancelled
        var ex = Assert.Throws<InvalidOperationException>(() => order.Cancel("Duplicate cancel", Now.AddMinutes(1)));
        Assert.Equal(OrderActionDenialReasons.OrderAlreadyCancelled, ex.Message);
    }

    [Fact]
    public void Prepare_TransitionsConfirmedToProcessing_AndFulfillmentToReady()
    {
        var order = CreateOrder();

        // Cannot prepare before confirmation
        var ex = Assert.Throws<InvalidOperationException>(() => order.Prepare(Now));
        Assert.Equal(OrderActionDenialReasons.OrderNotConfirmed, ex.Message);

        order.Confirm(Now);
        order.Prepare(Now.AddMinutes(5));

        Assert.Equal(OrderStatus.Processing, order.Status);
        Assert.Equal(FulfilmentStatus.Ready, order.FulfilmentStatus);
        Assert.Equal(Now.AddMinutes(5), order.ModifiedAt);
    }

    [Fact]
    public void CompleteFulfillmentFlow_Succeeds()
    {
        var order = CreateOrder(paymentMethod: OrderPaymentMethod.CashOnDelivery);
        order.Confirm(Now);
        order.Prepare(Now.AddMinutes(5));
        order.Dispatch(Now.AddMinutes(10));

        Assert.Equal(OrderStatus.Processing, order.Status);
        Assert.Equal(FulfilmentStatus.Dispatched, order.FulfilmentStatus);

        order.Deliver(Now.AddMinutes(30));
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(FulfilmentStatus.Delivered, order.FulfilmentStatus);

        order.MarkCodCollected(Now.AddMinutes(35));
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public void MarkDeliveryFailed_TransitionsDispatchedToFailed_AndRequiresReason()
    {
        var order = CreateOrder();
        order.Confirm(Now);
        order.Prepare(Now);
        order.Dispatch(Now);

        Assert.Throws<ArgumentException>(() => order.MarkDeliveryFailed("", Now));

        order.MarkDeliveryFailed("Customer unreachable at phone number", Now.AddMinutes(15));
        Assert.Equal(FulfilmentStatus.Failed, order.FulfilmentStatus);
    }

    [Fact]
    public void MerchantQrPaymentFlow_TransitionsProperly()
    {
        var order = CreateOrder(paymentMethod: OrderPaymentMethod.MerchantQr, codAvailable: false);
        Assert.Equal(PaymentStatus.AwaitingVerification, order.PaymentStatus);

        order.Confirm(Now);
        order.VerifyPayment(Now.AddMinutes(2));
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);

        // Cannot verify again
        var ex = Assert.Throws<InvalidOperationException>(() => order.VerifyPayment(Now.AddMinutes(3)));
        Assert.Equal(OrderActionDenialReasons.PaymentAlreadyPaid, ex.Message);
    }

    [Fact]
    public void MerchantQrPaymentRejection_RequiresReason()
    {
        var order = CreateOrder(paymentMethod: OrderPaymentMethod.MerchantQr, codAvailable: false);

        Assert.Throws<ArgumentException>(() => order.RejectPayment("no", Now));

        order.RejectPayment("QR slip amount does not match total", Now);
        Assert.Equal(PaymentStatus.Failed, order.PaymentStatus);
    }
}

