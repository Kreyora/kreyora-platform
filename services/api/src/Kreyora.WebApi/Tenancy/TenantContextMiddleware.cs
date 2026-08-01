using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Tenancy;
using Kreyora.Infrastructure.Errors;
using Microsoft.AspNetCore.Http;

namespace Kreyora.WebApi.Tenancy;

public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public const string TenantHeaderName = "X-Kreyora-Tenant-Id";

    public async Task InvokeAsync(
        HttpContext httpContext,
        ITenantContextAccessor tenantContext,
        ITenantContextResolutionService resolver,
        ICorrelationContext correlation)
    {
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<RequireTenantContextAttribute>() is null
            || httpContext.User.Identity?.IsAuthenticated is not true)
        {
            await next(httpContext);
            return;
        }

        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            await WriteProblemAsync(httpContext, StatusCodes.Status401Unauthorized, "Authentication required.", correlation.CorrelationId);
            return;
        }

        if (!httpContext.Request.Headers.TryGetValue(TenantHeaderName, out var requestedTenant)
            || requestedTenant.Count != 1
            || !IsUlid(requestedTenant[0]))
        {
            await WriteProblemAsync(httpContext, StatusCodes.Status400BadRequest, "A valid X-Kreyora-Tenant-Id header is required.", correlation.CorrelationId);
            return;
        }

        var context = await resolver.ResolveMembershipContextAsync(userId, requestedTenant[0]!, httpContext.RequestAborted);
        if (context is null)
        {
            await WriteProblemAsync(httpContext, StatusCodes.Status403Forbidden, "The selected workspace is unavailable.", correlation.CorrelationId);
            return;
        }

        using (tenantContext.BeginScope(context))
        {
            await next(httpContext);
        }
    }

    private static bool IsUlid(string? value)
    {
        if (value is null || value.Length != 26)
        {
            return false;
        }

        return Regex.IsMatch(value, "^[0-9A-HJKMNP-TV-Z]{26}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static Task WriteProblemAsync(HttpContext context, int status, string detail, string correlationId)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var title = status switch
        {
            StatusCodes.Status400BadRequest => "Validation Error",
            StatusCodes.Status401Unauthorized => "Unauthorized",
            _ => "Forbidden"
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(
            ProblemDetailsFactory.Create(status, title, detail, correlationId)));
    }
}
