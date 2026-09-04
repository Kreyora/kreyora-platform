using Kreyora.Domain.Common;

namespace Kreyora.Domain.Storefront;

public sealed class CheckoutSessionItem : BaseEntity, ITenantOwned
{
    private CheckoutSessionItem() { }
    public string TenantId { get; private set; } = string.Empty;
    public string CheckoutSessionId { get; private set; } = string.Empty;
    public string InventoryReservationId { get; private set; } = string.Empty;
    public string ProductId { get; private set; } = string.Empty;
    public string ProductTitle { get; private set; } = string.Empty;
    public string VariantId { get; private set; } = string.Empty;
    public string VariantName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPriceNpr { get; private set; }
    public decimal LineSubtotalNpr { get; private set; }

    public static CheckoutSessionItem Create(string tenantId, string sessionId, string reservationId, string productId, string productTitle, string variantId, string variantName, int quantity, decimal unitPriceNpr) => new()
    {
        TenantId = Require(tenantId, nameof(tenantId), 26), CheckoutSessionId = Require(sessionId, nameof(sessionId), 26), InventoryReservationId = Require(reservationId, nameof(reservationId), 26),
        ProductId = Require(productId, nameof(productId), 26), ProductTitle = Require(productTitle, nameof(productTitle), 160), VariantId = Require(variantId, nameof(variantId), 26), VariantName = Require(variantName, nameof(variantName), 160),
        Quantity = quantity > 0 ? quantity : throw new ArgumentOutOfRangeException(nameof(quantity)), UnitPriceNpr = unitPriceNpr > 0 ? unitPriceNpr : throw new ArgumentOutOfRangeException(nameof(unitPriceNpr)), LineSubtotalNpr = unitPriceNpr * quantity
    };
    private static string Require(string value, string parameterName, int maximumLength) { var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim(); return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized; }
}
