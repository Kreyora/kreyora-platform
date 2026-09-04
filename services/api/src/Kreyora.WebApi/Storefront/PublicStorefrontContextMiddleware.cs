using Kreyora.Application.Abstractions;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Storefront;
using Kreyora.Infrastructure.Errors;
using Kreyora.WebApi.Configuration;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Kreyora.WebApi.Storefront;

public sealed class PublicStorefrontContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        ICorrelationContext correlation)
    {
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<RequirePublicStorefrontContextAttribute>() is null)
        {
            await next(httpContext);
            return;
        }

        var options = httpContext.RequestServices.GetRequiredService<IOptions<PublicStorefrontOptions>>();
        var resolver = httpContext.RequestServices.GetRequiredService<IPublicStorefrontResolver>();
        var publicContext = httpContext.RequestServices.GetRequiredService<IPublicStorefrontContextAccessor>();
        var tenantContext = httpContext.RequestServices.GetRequiredService<ITenantContextAccessor>();
        var environment = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

        if (HttpMethods.IsPost(httpContext.Request.Method) || HttpMethods.IsPut(httpContext.Request.Method) || HttpMethods.IsPatch(httpContext.Request.Method))
        {
            var maxBodySize = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (maxBodySize is { IsReadOnly: false }) maxBodySize.MaxRequestBodySize = options.Value.WriteBodyLimitBytes;
            if (httpContext.Request.ContentLength.HasValue && httpContext.Request.ContentLength.Value > options.Value.WriteBodyLimitBytes)
            {
                httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                httpContext.Response.ContentType = "application/problem+json";
                httpContext.Response.Headers.CacheControl = "no-store";
                await httpContext.Response.WriteAsJsonAsync(ProblemDetailsFactory.Create(StatusCodes.Status413PayloadTooLarge, "Payload Too Large", "The request is too large.", correlation.CorrelationId));
                return;
            }
        }

        var slug = ResolveSlug(httpContext, options.Value, environment);
        PublicStorefrontContext? context = null;
        if (slug is not null)
        {
            try
            {
                context = await resolver.ResolveAsync(slug, httpContext.RequestAborted);
            }
            catch (ArgumentException)
            {
                context = null;
            }
        }

        if (context is null)
        {
            await WriteUnavailableAsync(httpContext, correlation.CorrelationId);
            return;
        }

        using (publicContext.BeginScope(context))
        using (tenantContext.BeginScope(new TenantContext(context.TenantId, null, null, null)))
        {
            // Read actions deliberately overwrite this after they create a safe public projection.
            // Validation, model-binding, authorization, and write failures must remain non-cacheable.
            httpContext.Response.Headers.CacheControl = "no-store";
            await next(httpContext);
        }
    }

    private static string? ResolveSlug(HttpContext context, PublicStorefrontOptions options, IWebHostEnvironment environment)
    {
        var requestedSlug = context.Request.RouteValues.TryGetValue("slug", out var routeValue) ? routeValue?.ToString() : null;
        if (!string.IsNullOrWhiteSpace(requestedSlug))
        {
            return options.EnableDevelopmentSlugRoutes && (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
                ? Store.NormalizePlatformSlug(requestedSlug)
                : null;
        }

        var host = context.Request.Host.Host.TrimEnd('.').ToLowerInvariant();
        var baseDomain = options.PlatformBaseDomain.TrimEnd('.').ToLowerInvariant();
        var suffix = $".{baseDomain}";
        if (host.Length <= suffix.Length || !host.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var slug = host[..^suffix.Length];
        return slug.Contains('.', StringComparison.Ordinal) ? null : Store.NormalizePlatformSlug(slug);
    }

    private static Task WriteUnavailableAsync(HttpContext context, string correlationId)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(ProblemDetailsFactory.NotFound("The storefront is unavailable.", correlationId));
    }
}
