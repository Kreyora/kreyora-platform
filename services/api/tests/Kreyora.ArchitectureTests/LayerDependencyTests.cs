using NetArchTest.Rules;

namespace Kreyora.ArchitectureTests;

public class LayerDependencyTests
{
    private const string DomainNamespace = "Kreyora.Domain";
    private const string ApplicationNamespace = "Kreyora.Application";
    private const string InfrastructureNamespace = "Kreyora.Infrastructure";
    private const string WebApiNamespace = "Kreyora.WebApi";

    [Fact]
    public void Domain_ShouldNotDependOn_Application()
    {
        var result = Types.InAssembly(typeof(Domain.DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ApplicationNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain must not reference Application. Offending types: {FormatTypes(result)}");
    }

    [Fact]
    public void Domain_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Domain.DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain must not reference Infrastructure. Offending types: {FormatTypes(result)}");
    }

    [Fact]
    public void Domain_ShouldNotDependOn_WebApi()
    {
        var result = Types.InAssembly(typeof(Domain.DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn(WebApiNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain must not reference WebApi. Offending types: {FormatTypes(result)}");
    }

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Application.ApplicationAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Application must not reference Infrastructure. Offending types: {FormatTypes(result)}");
    }

    [Fact]
    public void Application_ShouldNotDependOn_WebApi()
    {
        var result = Types.InAssembly(typeof(Application.ApplicationAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn(WebApiNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Application must not reference WebApi. Offending types: {FormatTypes(result)}");
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOn_WebApi()
    {
        var result = Types.InAssembly(typeof(Infrastructure.InfrastructureAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn(WebApiNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Infrastructure must not reference WebApi. Offending types: {FormatTypes(result)}");
    }

    private static string FormatTypes(TestResult result)
    {
        if (result.FailingTypes == null || !result.FailingTypes.Any())
            return "(none)";
        return string.Join(", ", result.FailingTypes.Select(t => t.FullName));
    }
}
