namespace Kreyora.WebApi.Storefront;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequirePublicStorefrontContextAttribute : Attribute;
