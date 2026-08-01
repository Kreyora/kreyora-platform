using System.Net;
using System.Text.Json;
using Kreyora.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.Errors;

public sealed partial class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation)
    {
        try
        {
            await _next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogExpectedRejection(context, ex.Message);
            await WriteProblemAsync(context, ProblemDetailsFactory.Forbidden("You are not authorized to perform this action.", correlation.CorrelationId));
        }
        catch (InvalidOperationException ex)
        {
            LogExpectedRejection(context, ex.Message);
            await WriteProblemAsync(context, ProblemDetailsFactory.Validation(ex.Message, correlationId: correlation.CorrelationId));
        }
        catch (Exception ex)
        {
            LogUnhandledException(_logger, context.Request.Method, context.Request.Path, ex);

            var detail = _env.IsDevelopment() ? ex.ToString() : "An unexpected error occurred.";
            await WriteProblemAsync(context, ProblemDetailsFactory.ServerError(detail, correlation.CorrelationId));
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, Microsoft.AspNetCore.Mvc.ProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status ?? (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    private void LogExpectedRejection(HttpContext context, string reason)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? string.Empty;
            LogExpectedException(_logger, method, path, reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandledException(ILogger logger, string method, string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Expected request rejection for {Method} {Path}: {Reason}")]
    private static partial void LogExpectedException(ILogger logger, string method, string path, string reason);
}
