using Kreyora.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Kreyora.WebApi.Controllers;

internal static class ResultExtensions
{
    public static ActionResult<T> ToActionResult<T>(this ControllerBase controller, Result<T> result) => result.Match<ActionResult<T>>(
        value => controller.Ok(value),
        error => new ObjectResult(new ProblemDetails { Type = error.Type, Title = error.Title, Status = error.Status, Detail = error.Detail }) { StatusCode = error.Status });
}
