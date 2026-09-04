using Kreyora.Domain.Common;

namespace Kreyora.Domain.Storefront;

public sealed class DeliveryRuleZone : BaseEntity, ITenantOwned
{
    public const int LocationMaxLength = 120;

    private DeliveryRuleZone()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string DeliveryRuleId { get; private set; } = string.Empty;
    public string District { get; private set; } = string.Empty;
    public string NormalizedDistrict { get; private set; } = string.Empty;
    public string? Municipality { get; private set; }
    public string? NormalizedMunicipality { get; private set; }
    public string? Locality { get; private set; }
    public string? NormalizedLocality { get; private set; }

    public int Specificity => NormalizedLocality is not null ? 3 : NormalizedMunicipality is not null ? 2 : 1;

    internal static DeliveryRuleZone Create(string tenantId, string deliveryRuleId, DeliveryZoneInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var district = Require(input.District, nameof(input.District));
        var municipality = Optional(input.Municipality);
        var locality = Optional(input.Locality);
        return new DeliveryRuleZone
        {
            TenantId = RequireId(tenantId, nameof(tenantId)),
            DeliveryRuleId = RequireId(deliveryRuleId, nameof(deliveryRuleId)),
            District = district,
            NormalizedDistrict = Normalize(district),
            Municipality = municipality,
            NormalizedMunicipality = municipality is null ? null : Normalize(municipality),
            Locality = locality,
            NormalizedLocality = locality is null ? null : Normalize(locality)
        };
    }

    public bool Matches(DeliveryDestination destination) =>
        string.Equals(NormalizedDistrict, destination.NormalizedDistrict, StringComparison.Ordinal) &&
        (NormalizedMunicipality is null || string.Equals(NormalizedMunicipality, destination.NormalizedMunicipality, StringComparison.Ordinal)) &&
        (NormalizedLocality is null || string.Equals(NormalizedLocality, destination.NormalizedLocality, StringComparison.Ordinal));

    public static DeliveryDestination NormalizeDestination(DeliveryDestinationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!string.Equals(Require(input.CountryCode, nameof(input.CountryCode)), "NP", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only Nepal delivery destinations are supported.", nameof(input));
        }

        var district = Require(input.District, nameof(input.District));
        var municipality = Optional(input.Municipality);
        var locality = Optional(input.Locality);
        return new DeliveryDestination("NP", district, Normalize(district), municipality, municipality is null ? null : Normalize(municipality), locality, locality is null ? null : Normalize(locality));
    }

    private static string RequireId(string value, string parameterName) => Require(value, parameterName, 26);

    private static string Require(string value, string parameterName, int maximumLength = LocationMaxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.") : normalized;
    }

    private static string? Optional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Require(value, nameof(value));
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

public sealed record DeliveryZoneInput(string District, string? Municipality, string? Locality);
public sealed record DeliveryDestinationInput(string CountryCode, string District, string? Municipality, string? Locality);
public sealed record DeliveryDestination(string CountryCode, string District, string NormalizedDistrict, string? Municipality, string? NormalizedMunicipality, string? Locality, string? NormalizedLocality);
