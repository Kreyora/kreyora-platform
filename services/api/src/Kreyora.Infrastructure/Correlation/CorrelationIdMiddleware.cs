using Kreyora.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Kreyora.Infrastructure.Correlation;

public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming))
        {
            correlation.SetCorrelationId(incoming!);
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlation.CorrelationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlation.CorrelationId))
        {
            await _next(context);
        }
    }
}
