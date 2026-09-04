using Kreyora.Domain.Common;

namespace Kreyora.Domain.Storefront;

public sealed class CheckoutSession : BaseEntity, ITenantOwned
{
    private CheckoutSession() { }

    public string TenantId { get; private set; } = string.Empty;
    public string StoreId { get; private set; } = string.Empty;
    public string? CustomerId { get; private set; }
    public string QuoteTokenFingerprint { get; private set; } = string.Empty;
    public DateTimeOffset QuoteExpiresAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public CheckoutSessionState State { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string? CustomerEmail { get; private set; }
    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2 { get; private set; }
    public string District { get; private set; } = string.Empty;
    public string? Municipality { get; private set; }
    public string? Locality { get; private set; }
    public string? Landmark { get; private set; }
    public string PrivacyPolicyFingerprint { get; private set; } = string.Empty;
    public DateTimeOffset PrivacyAcknowledgedAt { get; private set; }
    public DateTimeOffset PiiReviewAt { get; private set; }
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
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public List<CheckoutSessionItem> Items { get; private set; } = [];

    public static CheckoutSession Create(CheckoutSessionCreation creation)
    {
        ArgumentNullException.ThrowIfNull(creation);
        var session = new CheckoutSession
        {
            TenantId = Require(creation.TenantId, nameof(creation.TenantId), 26),
            StoreId = Require(creation.StoreId, nameof(creation.StoreId), 26),
            CustomerId = Optional(creation.CustomerId, 26),
            QuoteTokenFingerprint = Require(creation.QuoteTokenFingerprint, nameof(creation.QuoteTokenFingerprint), 64),
            QuoteExpiresAt = creation.QuoteExpiresAt,
            ExpiresAt = creation.ExpiresAt,
            State = CheckoutSessionState.Active,
            CustomerName = Require(creation.CustomerName, nameof(creation.CustomerName), 160),
            CustomerPhone = Require(creation.CustomerPhone, nameof(creation.CustomerPhone), 24),
            CustomerEmail = Optional(creation.CustomerEmail, 320),
            AddressLine1 = Require(creation.AddressLine1, nameof(creation.AddressLine1), 160),
            AddressLine2 = Optional(creation.AddressLine2, 160),
            District = Require(creation.District, nameof(creation.District), 120),
            Municipality = Optional(creation.Municipality, 120),
            Locality = Optional(creation.Locality, 120),
            Landmark = Optional(creation.Landmark, 160),
            PrivacyPolicyFingerprint = Require(creation.PrivacyPolicyFingerprint, nameof(creation.PrivacyPolicyFingerprint), 64),
            PrivacyAcknowledgedAt = creation.Now,
            PiiReviewAt = creation.PiiReviewAt,
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
        if (session.QuoteExpiresAt < session.ExpiresAt || session.ExpiresAt <= creation.Now || session.PiiReviewAt <= creation.Now) throw new ArgumentOutOfRangeException(nameof(creation));
        return session;
    }

    public void AddItem(CheckoutSessionItem item)
    {
        if (item.CheckoutSessionId != Id || item.TenantId != TenantId || Items.Any(existing => existing.VariantId == item.VariantId)) throw new InvalidOperationException("Checkout session items must be unique and owned by the session.");
        Items.Add(item);
    }

    public void Expire(DateTimeOffset now)
    {
        EnsureActive();
        if (ExpiresAt > now) throw new InvalidOperationException("Only due checkout sessions can expire.");
        State = CheckoutSessionState.Expired;
        ExpiredAt = now;
    }

    private void EnsureActive()
    {
        if (State != CheckoutSessionState.Active) throw new InvalidOperationException("Only active checkout sessions can transition.");
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }

    private static string? Optional(string? value, int maximumLength) => string.IsNullOrWhiteSpace(value) ? null : Require(value, nameof(value), maximumLength);
}

public sealed record CheckoutSessionCreation(string TenantId, string StoreId, string? CustomerId, string QuoteTokenFingerprint, DateTimeOffset QuoteExpiresAt, DateTimeOffset ExpiresAt, string CustomerName, string CustomerPhone, string? CustomerEmail, string AddressLine1, string? AddressLine2, string District, string? Municipality, string? Locality, string? Landmark, string PrivacyPolicyFingerprint, DateTimeOffset PiiReviewAt, decimal MerchandiseSubtotalNpr, decimal DiscountNpr, decimal DeliveryFeeNpr, decimal TaxNpr, decimal ProviderFeeNpr, decimal PlatformFeeNpr, decimal TotalNpr, string Currency, string DeliveryRuleId, string DeliveryRuleName, string? EstimatedEtaText, bool CodAvailable, DateTimeOffset Now);
