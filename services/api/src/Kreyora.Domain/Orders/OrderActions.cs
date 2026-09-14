namespace Kreyora.Domain.Orders;

public enum OrderAction
{
    Confirm,
    Cancel,
    Prepare,
    Dispatch,
    Deliver,
    MarkDeliveryFailed,
    VerifyPayment,
    RejectPayment,
    MarkCodCollected
}

public sealed record OrderActionEvaluation(
    OrderAction Action,
    bool IsAllowed,
    string? DenialReason,
    bool RequiresReason,
    bool IsDestructive);

public static class OrderActionDenialReasons
{
    public const string OrderAlreadyCancelled = "Cancelled orders cannot be modified or fulfilled.";
    public const string OrderAlreadyFulfilled = "Fulfilled orders cannot be cancelled or modified.";
    public const string OrderNotPendingConfirmation = "Only orders awaiting confirmation can be confirmed.";
    public const string OrderNotConfirmed = "Order must be confirmed before fulfilment can begin.";
    public const string OrderNotReady = "Order fulfilment must be ready before dispatch.";
    public const string OrderNotDispatched = "Order must be dispatched before delivery can be marked.";
    public const string MerchantQrPaymentUnverified = "Merchant QR orders must be verified and paid before dispatch.";
    public const string PaymentNotAwaitingVerification = "Payment is not awaiting verification.";
    public const string PaymentMethodNotMerchantQr = "Payment verification is only applicable to Merchant QR orders.";
    public const string PaymentMethodNotCod = "COD collection is only applicable to Cash on Delivery orders.";
    public const string CodNotReadyForCollection = "COD collection cannot be recorded before the order is dispatched or delivered.";
    public const string PaymentAlreadyPaid = "Payment has already been marked as paid.";
    public const string DispatchedOrderCannotCancelDirectly = "Dispatched orders cannot be cancelled directly; record delivery failure first.";
    public const string ReasonRequired = "A valid reason (between 3 and 500 characters) is required for this action.";
    public const string RoleNotAuthorized = "You do not have permission to perform this action.";
}

