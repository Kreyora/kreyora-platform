using Kreyora.Application.Tenancy;

namespace Kreyora.Infrastructure.Tenancy;

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private TenantContext? current;

    public TenantContext? Current => current;

    public TenantContext RequireCurrent() => current
        ?? throw new InvalidOperationException("A verified tenant context is required for this operation.");

    public IDisposable BeginScope(TenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.TenantId))
        {
            throw new ArgumentException("Tenant context must include a tenant ID.", nameof(context));
        }

        var previous = current;
        current = context;
        return new Scope(this, previous);
    }

    private sealed class Scope(TenantContextAccessor owner, TenantContext? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            owner.current = previous;
            disposed = true;
        }
    }
}
