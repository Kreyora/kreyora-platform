using Kreyora.Domain.Common;

namespace Kreyora.Domain.Storefront;

public sealed class StorePaymentConfiguration : BaseEntity, ITenantOwned
{
    public const int InstructionsMaxLength = 1_000;

    private StorePaymentConfiguration() { }

    public string TenantId { get; private set; } = string.Empty;
    public string StoreId { get; private set; } = string.Empty;
    public bool CodEnabled { get; private set; }
    public bool MerchantQrEnabled { get; private set; }
    public string? MerchantQrInstructions { get; private set; }
    public string? MerchantQrMediaAssetId { get; private set; }

    public static StorePaymentConfiguration Create(
        string tenantId,
        string storeId,
        bool codEnabled,
        bool merchantQrEnabled,
        string? merchantQrInstructions = null,
        string? merchantQrMediaAssetId = null)
    {
        var config = new StorePaymentConfiguration
        {
            TenantId = Require(tenantId, nameof(tenantId), 26),
            StoreId = Require(storeId, nameof(storeId), 26)
        };

        config.Update(codEnabled, merchantQrEnabled, merchantQrInstructions, merchantQrMediaAssetId);
        return config;
    }

    public void Update(
        bool codEnabled,
        bool merchantQrEnabled,
        string? merchantQrInstructions,
        string? merchantQrMediaAssetId)
    {
        if (!codEnabled && !merchantQrEnabled)
        {
            throw new InvalidOperationException("At least one payment method (COD or Merchant QR) must be enabled for the store.");
        }

        CodEnabled = codEnabled;
        MerchantQrEnabled = merchantQrEnabled;
        MerchantQrInstructions = NormalizeInstructions(merchantQrInstructions);
        MerchantQrMediaAssetId = Optional(merchantQrMediaAssetId, 26);
    }

    private static string? NormalizeInstructions(string? instructions)
    {
        var normalized = Optional(instructions, InstructionsMaxLength);
        if (normalized is not null && (normalized.Contains('<', StringComparison.Ordinal) || normalized.Contains('>', StringComparison.Ordinal)))
        {
            throw new ArgumentException("Merchant QR instructions cannot contain HTML markup.", nameof(instructions));
        }

        return normalized;
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.") : normalized;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(nameof(value), $"Value cannot exceed {maximumLength} characters.") : normalized;
    }
}

