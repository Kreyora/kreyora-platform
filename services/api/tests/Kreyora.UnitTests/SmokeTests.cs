namespace Kreyora.UnitTests;

public class SmokeTests
{
    [Fact]
    public void DomainAssembly_CanBeLoaded()
    {
        Assert.NotNull(Domain.DomainAssemblyMarker.AssemblyName);
    }

    [Fact]
    public void ApplicationAssembly_CanBeLoaded()
    {
        Assert.NotNull(Application.ApplicationAssemblyMarker.AssemblyName);
    }
}
