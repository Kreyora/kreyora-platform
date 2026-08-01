namespace Kreyora.WebApi.Tenancy;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireTenantContextAttribute : Attribute;
