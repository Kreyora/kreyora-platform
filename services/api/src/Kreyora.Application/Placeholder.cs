namespace Kreyora.Application;

public static class ApplicationAssemblyMarker
{
    public static readonly string AssemblyName = typeof(ApplicationAssemblyMarker).Assembly.GetName().Name!;
}
