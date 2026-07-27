using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.Infrastructure.Errors;

public static class ProblemDetailsFactory
{
    public static ProblemDetails Create(int status, string title, string? detail = null, string? correlationId = null)
    {
        var problem = new ProblemDetails
        {
            Type = StatusToType(status),
            Title = title,
            Status = status,
            Detail = detail
        };

        if (correlationId is not null)
        {
            problem.Extensions["traceId"] = correlationId;
        }

        return problem;
    }

    public static ProblemDetails Validation(string detail, IDictionary<string, string[]>? errors = null, string? correlationId = null)
    {
        var problem = Create(StatusCodes.Status400BadRequest, "Validation Error", detail, correlationId);

        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }

    public static ProblemDetails NotFound(string detail, string? correlationId = null)
        => Create(StatusCodes.Status404NotFound, "Not Found", detail, correlationId);

    public static ProblemDetails Conflict(string detail, string? correlationId = null)
        => Create(StatusCodes.Status409Conflict, "Conflict", detail, correlationId);

    public static ProblemDetails Forbidden(string detail, string? correlationId = null)
        => Create(StatusCodes.Status403Forbidden, "Forbidden", detail, correlationId);

    public static ProblemDetails ServerError(string? detail = null, string? correlationId = null)
        => Create(StatusCodes.Status500InternalServerError, "Internal Server Error", detail, correlationId);

    private static string StatusToType(int status) => status switch
    {
        400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        403 => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        500 => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        _ => "https://tools.ietf.org/html/rfc9110#section-15"
    };
}
