using Kreyora.Domain.Common;

namespace Kreyora.Domain.Orders;

public sealed class Order : BaseEntity, ITenantOwned
{
    private Order() { }

    public string TenantId { get; private set; } = string.Empty;
    public string StoreId { get; private set; } = string.Empty;
    public string CheckoutSessionId { get; private set; } = string.Empty;
    public string? CustomerId { get; private set; }
    public string OrderNumber { get; private set; } = string.Empty;
    public OrderSource Source { get; private set; }
    public OrderPaymentMethod PaymentMethod { get; private set; }
    public OrderStatus Status { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public FulfilmentStatus FulfilmentStatus { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string? CustomerEmail { get; private set; }
    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2 { get; private set; }
    public string District { get; private set; } = string.Empty;
    public string? Municipality { get; private set; }
    public string? Locality { get; private set; }
    public string? Landmark { get; private set; }
    public decimal MerchandiseSubtotalNpr { get; private set; }
    public decimal DiscountNpr { get; private set; }
    public decimal DeliveryFeeNpr { get; private set; }
    public decimal TaxNpr { get; private set; }
    public decimal ProviderFeeNpr { get; private set; }
    public decimal PlatformFeeNpr { get; private set; }
    public decimal TotalNpr { get; private set; }
    public string Currency { get; private set; } = "NPR";
    public string DeliveryRuleId { get; private set; } = string.Empty;
    public string DeliveryRuleName { get; private set; } = string.Empty;
    public string? EstimatedEtaText { get; private set; }
    public bool CodAvailable { get; private set; }
    public List<OrderItem> Items { get; private set; } = [];

    public static Order Create(OrderCreation creation)
    {
        ArgumentNullException.ThrowIfNull(creation);
        if (!Enum.IsDefined(creation.PaymentMethod)) throw new ArgumentOutOfRangeException(nameof(creation));
        if (creation.PaymentMethod == OrderPaymentMethod.CashOnDelivery && !creation.CodAvailable) throw new InvalidOperationException("Cash on delivery is unavailable for this checkout session.");
        if (creation.TotalNpr < 0 || creation.MerchandiseSubtotalNpr < 0 || creation.DeliveryFeeNpr < 0) throw new ArgumentOutOfRangeException(nameof(creation));
        var order = new Order
        {
            TenantId = Require(creation.TenantId, nameof(creation.TenantId), 26),
            StoreId = Require(creation.StoreId, nameof(creation.StoreId), 26),
            CheckoutSessionId = Require(creation.CheckoutSessionId, nameof(creation.CheckoutSessionId), 26),
            CustomerId = Optional(creation.CustomerId, 26),
            Source = OrderSource.Storefront,
            PaymentMethod = creation.PaymentMethod,
            Status = OrderStatus.PendingConfirmation,
            PaymentStatus = creation.PaymentMethod == OrderPaymentMethod.CashOnDelivery ? PaymentStatus.Pending : PaymentStatus.AwaitingVerification,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            CustomerName = Require(creation.CustomerName, nameof(creation.CustomerName), 160),
            CustomerPhone = Require(creation.CustomerPhone, nameof(creation.CustomerPhone), 24),
            CustomerEmail = Optional(creation.CustomerEmail, 320),
            AddressLine1 = Require(creation.AddressLine1, nameof(creation.AddressLine1), 160),
            AddressLine2 = Optional(creation.AddressLine2, 160),
            District = Require(creation.District, nameof(creation.District), 120),
            Municipality = Optional(creation.Municipality, 120),
            Locality = Optional(creation.Locality, 120),
            Landmark = Optional(creation.Landmark, 160),
            MerchandiseSubtotalNpr = creation.MerchandiseSubtotalNpr,
            DiscountNpr = creation.DiscountNpr,
            DeliveryFeeNpr = creation.DeliveryFeeNpr,
            TaxNpr = creation.TaxNpr,
            ProviderFeeNpr = creation.ProviderFeeNpr,
            PlatformFeeNpr = creation.PlatformFeeNpr,
            TotalNpr = creation.TotalNpr,
            Currency = Require(creation.Currency, nameof(creation.Currency), 3),
            DeliveryRuleId = Require(creation.DeliveryRuleId, nameof(creation.DeliveryRuleId), 26),
            DeliveryRuleName = Require(creation.DeliveryRuleName, nameof(creation.DeliveryRuleName), 160),
            EstimatedEtaText = Optional(creation.EstimatedEtaText, 120),
            CodAvailable = creation.CodAvailable
        };
        order.OrderNumber = $"ORD-{order.Id}";
        return order;
    }

    public void AddItem(OrderItem item)
    {
        if (item.TenantId != TenantId || item.OrderId != Id || Items.Any(existing => existing.VariantId == item.VariantId)) throw new InvalidOperationException("Order items must be unique and owned by the order.");
        Items.Add(item);
    }

    public void Confirm(DateTimeOffset now)
    {
        EnsureTransitionAllowed(OrderAction.Confirm);
        Status = OrderStatus.Confirmed;
        ModifiedAt = now;
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        EnsureTransitionAllowed(OrderAction.Cancel);
        if (!OrderTransitionPolicy.ValidateReason(OrderAction.Cancel, reason, out var error))
            throw new ArgumentException(error, nameof(reason));

        Status = OrderStatus.Cancelled;
        if (FulfilmentStatus != FulfilmentStatus.Delivered)
        {
            FulfilmentStatus = FulfilmentStatus.Cancelled;
        }
        ModifiedAt = now;
    }

    public void Prepare(DateTimeOffset now)
    {
        EnsureTransitionAllowed(OrderAction.Prepare);
        FulfilmentStatus = FulfilmentStatus.Ready;
        if (Status == OrderStatus.Confirmed)
        {
            Status = OrderStatus.Processing;
        }
        ModifiedAt = now;
    }

    public void Dispatch(DateTimeOffset now)
    {
        EnsureTransitionAllowed(OrderAction.Dispatch);
        FulfilmentStatus = FulfilmentStatus.Dispatched;
        if (Status == OrderStatus.Confirmed)
        {
            Status = OrderStatus.Processing;
        }
        ModifiedAt = now;
    }

    public void Deliver(DateTimeOffset now)
    {
        EnsureTransitionAllowed(OrderAction.Deliver);
        FulfilmentStatus = FulfilmentStatus.Delivered;
        Status = OrderStatus.Fulfilled;
        ModifiedAt = now;
    }

    public void MarkDeliveryFailed(string reason, DateTimeOffset now)
    {
        EnsureTransitionAllowed(OrderAction.MarkDeliveryFailed);
        if (!OrderTransitionPolicy.ValidateReason(OrderAction.MarkDeliveryFailed, reason, out var error))
            throw new ArgumentException(error, nameof(reason));

        FulfilmentStatus = FulfilmentStatus.Failed;
        ModifiedAt = now;
    }

    public void VerifyPayment(DateTimeOffset now)
    {
        EnsureTransitionAllowed(OrderAction.VerifyPayment);
        PaymentStatus = PaymentStatus.Paid;
        ModifiedAt = now;
    }

    public void RejectPayment(string reason, DateTimeOffset now)
    {
        EnsureTransitionAllowed(OrderAction.RejectPayment);
        if (!OrderTransitionPolicy.ValidateReason(OrderAction.RejectPayment, reason, out var error))
            throw new ArgumentException(error, nameof(reason));

        PaymentStatus = PaymentStatus.Failed;
        ModifiedAt = now;
    }

    public void MarkCodCollected(DateTimeOffset now)
    {
        EnsureTransitionAllowed(OrderAction.MarkCodCollected);
        PaymentStatus = PaymentStatus.Paid;
        ModifiedAt = now;
    }

    private void EnsureTransitionAllowed(OrderAction action)
    {
        if (!OrderTransitionPolicy.CanExecute(this, action, Tenancy.TenantRole.Owner, out var denialReason))
        {
            throw new InvalidOperationException(denialReason);
        }
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }

    private static string? Optional(string? value, int maximumLength) => string.IsNullOrWhiteSpace(value) ? null : Require(value, nameof(value), maximumLength);
}

public sealed record OrderCreation(string TenantId, string StoreId, string CheckoutSessionId, string? CustomerId, OrderPaymentMethod PaymentMethod,
    string CustomerName, string CustomerPhone, string? CustomerEmail, string AddressLine1, string? AddressLine2, string District, string? Municipality,
    string? Locality, string? Landmark, decimal MerchandiseSubtotalNpr, decimal DiscountNpr, decimal DeliveryFeeNpr, decimal TaxNpr,
    decimal ProviderFeeNpr, decimal PlatformFeeNpr, decimal TotalNpr, string Currency, string DeliveryRuleId, string DeliveryRuleName,
    string? EstimatedEtaText, bool CodAvailable);
