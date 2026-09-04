using Kreyora.Application.Storefront;

namespace Kreyora.Infrastructure.Storefront;

public sealed class PublicStorefrontContextAccessor : IPublicStorefrontContextAccessor
{
    private PublicStorefrontContext? current;

    public PublicStorefrontContext? Current => current;

    public PublicStorefrontContext RequireCurrent() => current
        ?? throw new InvalidOperationException("A verified public storefront context is required for this operation.");

    public IDisposable BeginScope(PublicStorefrontContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.TenantId) || string.IsNullOrWhiteSpace(context.StoreId) || string.IsNullOrWhiteSpace(context.PlatformSlug))
        {
            throw new ArgumentException("Public storefront context requires tenant, store, and slug values.", nameof(context));
        }

        var previous = current;
        current = context;
        return new Scope(this, previous);
    }

    private sealed class Scope(PublicStorefrontContextAccessor owner, PublicStorefrontContext? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            owner.current = previous;
            disposed = true;
        }
    }
}
