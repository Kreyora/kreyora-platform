namespace Kreyora.Domain.Orders;

public enum OrderStatus
{
    PendingConfirmation,
    Confirmed,
    Processing,
    Fulfilled,
    Cancelled
}

public enum PaymentStatus
{
    Pending,
    AwaitingVerification,
    Paid,
    Failed,
    Refunded
}

public enum FulfilmentStatus
{
    Unfulfilled,
    Ready,
    Dispatched,
    Delivered,
    Failed,
    Cancelled
}

public enum OrderSource
{
    Storefront
}

public enum OrderPaymentMethod
{
    CashOnDelivery,
    MerchantQr
}
