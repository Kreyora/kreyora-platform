using Kreyora.Domain.Storefront;

namespace Kreyora.UnitTests.Domain;

public sealed class DeliveryRuleTests
{
    private const string TenantId = "01J00000000000000000000001";
    private const string StoreId = "01J00000000000000000000002";

    [Fact]
    public void Create_NormalizesZones_AndCalculatesThresholdDeliveryFee()
    {
        var rule = DeliveryRule.Create(TenantId, StoreId, new DeliveryRuleSettings(
            "Kathmandu delivery",
            10,
            DeliveryFeeType.Threshold,
            150m,
            3_000m,
            "1-2 days",
            true,
            true,
            [new DeliveryZoneInput(" Kathmandu ", " Kathmandu Metropolitan City ", " Thamel ")]));

        var zone = Assert.Single(rule.Zones);

        Assert.Equal("Kathmandu", zone.District);
        Assert.Equal("KATHMANDU", zone.NormalizedDistrict);
        Assert.Equal(3, zone.Specificity);
        Assert.Equal(150m, rule.CalculateFee(2_999m));
        Assert.Equal(0m, rule.CalculateFee(3_000m));
    }

    [Fact]
    public void Create_RejectsInvalidThresholdAndDuplicateNormalizedZones()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryRule.Create(TenantId, StoreId, new DeliveryRuleSettings(
            "Invalid threshold", 0, DeliveryFeeType.Threshold, 100m, 0m, null, false, true,
            [new DeliveryZoneInput("Kathmandu", null, null)])));

        Assert.Throws<ArgumentException>(() => DeliveryRule.Create(TenantId, StoreId, new DeliveryRuleSettings(
            "Duplicate coverage", 0, DeliveryFeeType.Flat, 100m, null, null, false, true,
            [
                new DeliveryZoneInput("Kathmandu", "KMC", null),
                new DeliveryZoneInput(" kathmandu ", " kmc ", null)
            ])));
    }

    [Fact]
    public void NormalizeDestination_AcceptsNepalAndRejectsOtherCountries()
    {
        var destination = DeliveryRuleZone.NormalizeDestination(new DeliveryDestinationInput("np", "Kathmandu", null, null));

        Assert.Equal("NP", destination.CountryCode);
        Assert.Equal("KATHMANDU", destination.NormalizedDistrict);
        Assert.Throws<ArgumentException>(() => DeliveryRuleZone.NormalizeDestination(new DeliveryDestinationInput("IN", "Darjeeling", null, null)));
    }
}
