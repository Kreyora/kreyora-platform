namespace Kreyora.Infrastructure;

public static class InfrastructureAssemblyMarker
{
    public static readonly string AssemblyName = typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name!;
}
