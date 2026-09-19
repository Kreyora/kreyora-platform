using System.Text;
using Asp.Versioning;
using Kreyora.Application.Integrations;
using Kreyora.Domain.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

[ApiController]
[AllowAnonymous]
[ApiVersion("1.0")]
public sealed class WebhooksController(IWebhookIngressService ingressService) : ControllerBase
{
    public const int MaxPayloadSizeBytes = 256 * 1024; // 256 KB

    [HttpPost("v{version:apiVersion}/webhooks/{channel}")]
    [HttpPost("v{version:apiVersion}/webhooks/{channel}/{connectionId}")]
    [RequestSizeLimit(MaxPayloadSizeBytes)]
    public async Task<IActionResult> HandlePost(
        string channel,
        string? connectionId,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ChannelType>(channel, ignoreCase: true, out var channelType))
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Channel Not Found",
                Detail = $"Unknown channel '{channel}'."
            });
        }

        // Read raw body bytes
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, cancellationToken);
        var rawBody = ms.ToArray();

        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var queryParams = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var correlationId = Request.Headers.TryGetValue("X-Correlation-ID", out var corr)
            ? corr.ToString()
            : Guid.NewGuid().ToString("N");

        var command = new WebhookIngressCommand(
            Channel: channelType,
            ConnectionId: connectionId,
            Method: Request.Method,
            Path: Request.Path,
            Headers: headers,
            QueryParameters: queryParams,
            RawBody: rawBody,
            ContentType: Request.ContentType,
            CorrelationId: correlationId,
            ReceivedAt: DateTimeOffset.UtcNow);

        var result = await ingressService.HandleWebhookAsync(command, cancellationToken);

        if (result.IsSuccess)
        {
            Response.Headers["X-Correlation-ID"] = correlationId;
            return new ContentResult
            {
                Content = result.ResponseBody ?? (result.IsDuplicate ? "{\"status\":\"duplicate\"}" : "{\"status\":\"accepted\"}"),
                ContentType = "application/json",
                StatusCode = result.StatusCode
            };
        }

        return StatusCode(result.StatusCode, new ProblemDetails
        {
            Status = result.StatusCode,
            Title = GetTitleForStatusCode(result.StatusCode),
            Detail = result.ErrorReason
        });
    }

    [HttpGet("v{version:apiVersion}/webhooks/{channel}")]
    [HttpGet("v{version:apiVersion}/webhooks/{channel}/{connectionId}")]
    public async Task<IActionResult> HandleGet(
        string channel,
        string? connectionId,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ChannelType>(channel, ignoreCase: true, out var channelType))
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Channel Not Found",
                Detail = $"Unknown channel '{channel}'."
            });
        }

        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var queryParams = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var command = new WebhookChallengeCommand(
            Channel: channelType,
            ConnectionId: connectionId,
            QueryParameters: queryParams,
            Headers: headers);

        var result = await ingressService.HandleChallengeAsync(command, cancellationToken);

        if (result.IsValid && !string.IsNullOrEmpty(result.ChallengeResponse))
        {
            return Content(result.ChallengeResponse, "text/plain", Encoding.UTF8);
        }

        return StatusCode(result.StatusCode, new ProblemDetails
        {
            Status = result.StatusCode,
            Title = "Challenge Verification Failed",
            Detail = result.ErrorReason
        });
    }

    private static string GetTitleForStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
        StatusCodes.Status415UnsupportedMediaType => "Unsupported Media Type",
        _ => "Internal Server Error"
    };
}

