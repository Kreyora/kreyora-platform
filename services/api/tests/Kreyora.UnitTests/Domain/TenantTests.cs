using Kreyora.Domain.Tenancy;

namespace Kreyora.UnitTests.Domain;

public class TenantTests
{
    [Fact]
    public void Create_NormalizesSlug_AndStartsInExpectedStates()
    {
        var tenant = Tenant.Create("  Kreyora Store  ", "  Kreyora-Store  ");

        Assert.Equal("Kreyora Store", tenant.DisplayName);
        Assert.Equal("kreyora-store", tenant.Slug);
        Assert.Equal("KREYORA-STORE", tenant.NormalizedSlug);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(OnboardingState.NotStarted, tenant.OnboardingState);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("tenant--store")]
    [InlineData("tenant_store")]
    [InlineData("tenant store")]
    public void Create_RejectsInvalidSlugs(string slug)
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create("Kreyora Store", slug));
    }

    [Fact]
    public void LifecycleStates_CanBeChangedWithoutReadinessValidation()
    {
        var tenant = Tenant.Create("Kreyora Store", "kreyora-store");

        tenant.SetStatus(TenantStatus.Suspended);
        tenant.SetOnboardingState(OnboardingState.Ready);

        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        Assert.Equal(OnboardingState.Ready, tenant.OnboardingState);
    }
}
