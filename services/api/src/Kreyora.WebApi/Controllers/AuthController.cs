using Asp.Versioning;
using Kreyora.Application.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kreyora.WebApi.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/auth")]
public sealed class AuthController(IAuthenticationService authenticationService, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth-registration")]
    public async Task<IActionResult> Register(RegisterOwnerRequest request, CancellationToken cancellationToken)
    {
        var result = await authenticationService.RegisterOwnerAsync(request, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created) : BadRequest(new { errors = result.Errors });
    }

    [HttpPost("sign-in")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth-sign-in")]
    public async Task<IActionResult> SignIn(SignInRequest request, CancellationToken cancellationToken)
    {
        var result = await authenticationService.SignInAsync(request, cancellationToken);
        return result.Succeeded ? NoContent() : Unauthorized(new { detail = "Invalid email or password." });
    }

    [HttpPost("sign-out")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutCurrentUser()
    {
        await authenticationService.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthenticatedUser>> GetCurrentUser(CancellationToken cancellationToken)
    {
        var user = await authenticationService.GetCurrentUserAsync(cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpPost("password-reset/request")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth-password-reset")]
    public async Task<IActionResult> RequestPasswordReset(PasswordResetRequestBody request, CancellationToken cancellationToken)
    {
        await authenticationService.RequestPasswordResetAsync(request.Email, cancellationToken);
        return Accepted(new { message = "If an account exists for that email address, password reset instructions will be sent." });
    }

    [HttpPost("password-reset/confirm")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth-password-reset")]
    public async Task<IActionResult> ConfirmPasswordReset(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await authenticationService.ResetPasswordAsync(request, cancellationToken);
        return result.Succeeded ? NoContent() : BadRequest(new { errors = result.Errors });
    }

    public sealed record PasswordResetRequestBody(string Email);
}
