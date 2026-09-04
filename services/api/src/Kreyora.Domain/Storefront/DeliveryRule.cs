using Kreyora.Domain.Common;

namespace Kreyora.Domain.Storefront;

public sealed class DeliveryRule : BaseEntity, ITenantOwned
{
    public const int NameMaxLength = 160;
    public const int EstimatedEtaMaxLength = 120;
    public const int MaximumZones = 50;
    private DeliveryRule()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string StoreId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Priority { get; private set; }
    public DeliveryFeeType FeeType { get; private set; }
    public decimal BaseFeeNpr { get; private set; }
    public decimal? FreeAboveNpr { get; private set; }
    public string? EstimatedEtaText { get; private set; }
    public bool CodAvailable { get; private set; }
    public bool IsActive { get; private set; }
    public List<DeliveryRuleZone> Zones { get; private set; } = [];

    public static DeliveryRule Create(string tenantId, string storeId, DeliveryRuleSettings settings)
    {
        var rule = new DeliveryRule
        {
            TenantId = RequireId(tenantId, nameof(tenantId)),
            StoreId = RequireId(storeId, nameof(storeId))
        };
        rule.Update(settings);
        return rule;
    }

    public void Update(DeliveryRuleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Name = Require(settings.Name, nameof(settings.Name), NameMaxLength);
        if (settings.Priority is < 0 or > 10_000) throw new ArgumentOutOfRangeException(nameof(settings), "Priority must be between 0 and 10000.");
        if (settings.BaseFeeNpr < 0 || HasMoreThanTwoDecimals(settings.BaseFeeNpr)) throw new ArgumentOutOfRangeException(nameof(settings), "Base delivery fee must be non-negative NPR with at most two decimal places.");
        if (settings.FeeType == DeliveryFeeType.Threshold)
        {
            if (settings.FreeAboveNpr is null || settings.FreeAboveNpr <= 0 || HasMoreThanTwoDecimals(settings.FreeAboveNpr.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "Threshold rules require a positive NPR free-delivery threshold.");
            }
        }
        else if (settings.FreeAboveNpr is not null)
        {
            throw new ArgumentException("Flat delivery rules cannot define a free-delivery threshold.", nameof(settings));
        }

        if (settings.Zones is null || settings.Zones.Count is 0 or > MaximumZones) throw new ArgumentException($"A delivery rule requires 1-{MaximumZones} zones.", nameof(settings));
        var nextZones = settings.Zones.Select(input => DeliveryRuleZone.Create(TenantId, Id, input)).ToArray();
        if (nextZones.GroupBy(zone => $"{zone.NormalizedDistrict}|{zone.NormalizedMunicipality}|{zone.NormalizedLocality}", StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Delivery zones must be unique within a rule.", nameof(settings));
        }

        Priority = settings.Priority;
        FeeType = settings.FeeType;
        BaseFeeNpr = settings.BaseFeeNpr;
        FreeAboveNpr = settings.FreeAboveNpr;
        EstimatedEtaText = Optional(settings.EstimatedEtaText, EstimatedEtaMaxLength);
        CodAvailable = settings.CodAvailable;
        IsActive = settings.IsActive;
        Zones.Clear();
        Zones.AddRange(nextZones);
    }

    public decimal CalculateFee(decimal merchandiseSubtotalNpr) =>
        FeeType == DeliveryFeeType.Threshold && merchandiseSubtotalNpr >= FreeAboveNpr!.Value ? 0m : BaseFeeNpr;

    private static bool HasMoreThanTwoDecimals(decimal value) => decimal.Truncate(value * 100m) != value * 100m;
    private static string RequireId(string value, string parameterName) => Require(value, parameterName, 26);

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.") : normalized;
    }

    private static string? Optional(string? value, int maximumLength) => string.IsNullOrWhiteSpace(value) ? null : Require(value, nameof(value), maximumLength);
}

public sealed record DeliveryRuleSettings(
    string Name,
    int Priority,
    DeliveryFeeType FeeType,
    decimal BaseFeeNpr,
    decimal? FreeAboveNpr,
    string? EstimatedEtaText,
    bool CodAvailable,
    bool IsActive,
    IReadOnlyList<DeliveryZoneInput> Zones);
