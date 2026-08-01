using System.Security.Claims;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Kreyora.IntegrationTests.Tenancy;

public class TenantContextMiddlewareTests
{
    [Fact]
    public async Task MissingOrForgedHeader_IsRejected_WithoutExecutingTheEndpoint()
    {
        var accessor = new TenantContextAccessor();
        var executed = false;
        var middleware = new TenantContextMiddleware(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        var missing = CreateProtectedRequest();
        await middleware.InvokeAsync(missing, accessor, new StubResolver(null), new StubCorrelationContext());
        Assert.Equal(StatusCodes.Status400BadRequest, missing.Response.StatusCode);
        Assert.False(executed);

        var forged = CreateProtectedRequest();
        forged.Request.Headers[TenantContextMiddleware.TenantHeaderName] = IdGenerator.NewId();
        await middleware.InvokeAsync(forged, accessor, new StubResolver(null), new StubCorrelationContext());
        Assert.Equal(StatusCodes.Status403Forbidden, forged.Response.StatusCode);
        Assert.False(executed);
        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task VerifiedSelection_IsAvailableOnlyDuringTheProtectedRequest()
    {
        var accessor = new TenantContextAccessor();
        var tenantId = IdGenerator.NewId();
        TenantContext? observed = null;
        var middleware = new TenantContextMiddleware(_ =>
        {
            observed = accessor.Current;
            return Task.CompletedTask;
        });
        var request = CreateProtectedRequest();
        request.Request.Headers[TenantContextMiddleware.TenantHeaderName] = tenantId;

        await middleware.InvokeAsync(
            request,
            accessor,
            new StubResolver(new TenantContext(tenantId, "user-1", "membership-1", TenantRole.Owner)),
            new StubCorrelationContext());

        Assert.Equal(tenantId, observed!.TenantId);
        Assert.Equal("user-1", observed.UserId);
        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task ActiveSupportGrant_EstablishesAReadOnlyContext()
    {
        var accessor = new TenantContextAccessor();
        var tenantId = IdGenerator.NewId();
        TenantContext? observed = null;
        var middleware = new TenantContextMiddleware(_ =>
        {
            observed = accessor.Current;
            return Task.CompletedTask;
        });
        var request = CreateProtectedRequest();
        request.Request.Headers[TenantContextMiddleware.TenantHeaderName] = tenantId;

        await middleware.InvokeAsync(
            request,
            accessor,
            new StubResolver(null, new TenantContext(tenantId, "user-1", null, null, "grant-1")),
            new StubCorrelationContext());

        Assert.True(observed!.IsReadOnlySupport);
        Assert.Null(accessor.Current);
    }

    private static DefaultHttpContext CreateProtectedRequest()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-1")],
            "test"));
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new RequireTenantContextAttribute()),
            "tenant-context-test"));
        return context;
    }

    private sealed class StubResolver(TenantContext? resolution, TenantContext? supportResolution = null) : ITenantContextResolutionService
    {
        public Task<IReadOnlyList<WorkspaceSummary>> GetActiveWorkspacesAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkspaceSummary>>([]);

        public Task<TenantContext?> ResolveMembershipContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(resolution);

        public Task<TenantContext?> ResolveBackgroundContextAsync(string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<TenantContext?>(null);

        public Task<TenantContext?> ResolveSupportContextAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(supportResolution);
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "tenant-context-test";

        public void SetCorrelationId(string correlationId)
        {
        }
    }
}
