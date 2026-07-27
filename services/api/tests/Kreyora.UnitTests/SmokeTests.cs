namespace Kreyora.UnitTests;

public class SmokeTests
{
    [Fact]
    public void DomainAssembly_CanBeLoaded()
    {
        Assert.NotNull(Kreyora.Domain.DomainAssemblyMarker.AssemblyName);
    }

    [Fact]
    public void ApplicationAssembly_CanBeLoaded()
    {
        Assert.NotNull(Kreyora.Application.ApplicationAssemblyMarker.AssemblyName);
    }

    [Fact]
    public void InfrastructureAssembly_CanBeLoaded()
    {
        Assert.NotNull(Kreyora.Infrastructure.InfrastructureAssemblyMarker.AssemblyName);
    }
}
