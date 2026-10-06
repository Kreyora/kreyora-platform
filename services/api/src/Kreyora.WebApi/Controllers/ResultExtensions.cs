using Kreyora.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

internal static class ResultExtensions
{
    public static ActionResult<T> ToActionResult<T>(this ControllerBase controller, Result<T> result) => result.Match<ActionResult<T>>(
        value => controller.Ok(value),
        error => new ObjectResult(ToProblem(error)) { StatusCode = error.Status });

    /// <summary>RFC 7807 problem; field-level errors, when present, use the standard validation-problem <c>errors</c> member.</summary>
    private static ProblemDetails ToProblem(ErrorDetail error) =>
        error.Errors is { Count: > 0 } errors
            ? new ValidationProblemDetails(errors) { Type = error.Type, Title = error.Title, Status = error.Status, Detail = error.Detail }
            : new ProblemDetails { Type = error.Type, Title = error.Title, Status = error.Status, Detail = error.Detail };
}
