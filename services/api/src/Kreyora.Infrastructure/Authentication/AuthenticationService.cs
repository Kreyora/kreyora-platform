using System.Data;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Kreyora.Application.Authentication;
using Kreyora.Application.Messaging;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Email;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Authentication;

public sealed class AuthenticationService(
    AppDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IHttpContextAccessor httpContextAccessor,
    IEmailSender emailSender,
    IOptions<SmtpEmailOptions> emailOptions,
    ILogger<AuthenticationService> logger) : IAuthenticationService
{
    private static readonly Action<ILogger, string, Exception?> PasswordResetEmailDeliveryFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1001, "PasswordResetEmailDeliveryFailed"),
            "Password reset email delivery failed ({FailureType}).");

    public async Task<RegistrationResult> RegisterOwnerAsync(RegisterOwnerRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var user = new ApplicationUser
        {
            DisplayName = request.DisplayName,
            Email = request.Email.Trim(),
            UserName = request.Email.Trim()
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RegistrationResult(false, createResult.Errors.Select(error => error.Description).ToArray());
        }

        var tenant = Tenant.Create(request.TenantDisplayName, request.TenantSlug);
        dbContext.Tenants.Add(tenant);
        dbContext.Memberships.Add(Membership.Grant(tenant.Id, user.Id, TenantRole.Owner));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await signInManager.SignInAsync(user, isPersistent: false);
            return new RegistrationResult(true, []);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RegistrationResult(false, ["Unable to create this account."]);
        }
    }

    public async Task<Kreyora.Application.Authentication.SignInResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
    {
        var result = await signInManager.PasswordSignInAsync(
            request.Email.Trim(),
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        return new Kreyora.Application.Authentication.SignInResult(result.Succeeded, result.IsLockedOut);
    }

    public Task SignOutAsync() => signInManager.SignOutAsync();

    public async Task<AuthenticatedUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(userId);
        return user is null
            ? null
            : new AuthenticatedUser(user.Id, user.DisplayName, user.Email!);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        try
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            await emailSender.SendAsync(CreatePasswordResetEmail(user.Email, token), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PasswordResetEmailDeliveryFailed(logger, exception.GetType().Name, null);
        }
    }

    public async Task<PasswordResetResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return new PasswordResetResult(false, ["Unable to reset the password."]);
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        return new PasswordResetResult(
            result.Succeeded,
            result.Errors.Select(error => error.Description).ToArray());
    }

    private EmailMessage CreatePasswordResetEmail(string recipientEmail, string token)
    {
        var settings = emailOptions.Value;
        var resetUrl = QueryHelpers.AddQueryString(
            new Uri(new Uri(settings.ApplicationPublicUrl.TrimEnd('/') + "/"), "recover/reset").ToString(),
            new Dictionary<string, string?>
            {
                ["email"] = recipientEmail,
                ["token"] = token
            });
        var encodedUrl = HtmlEncoder.Default.Encode(resetUrl);
        var subject = $"Reset your {settings.ApplicationName} password";
        var lifetime = settings.PasswordResetTokenLifetimeMinutes;
        var textBody = $"We received a request to reset your {settings.ApplicationName} password. " +
            $"Use this link within {lifetime} minutes:\n\n{resetUrl}\n\n" +
            "If you did not request this, you can safely ignore this email.";
        var htmlBody = $"<p>We received a request to reset your {HtmlEncoder.Default.Encode(settings.ApplicationName)} password.</p>" +
            $"<p><a href=\"{encodedUrl}\">Reset your password</a></p>" +
            $"<p>This link expires in {lifetime} minutes.</p>" +
            "<p>If you did not request this, you can safely ignore this email.</p>" +
            $"<p>If the button does not work, copy and paste this URL into your browser:<br>{encodedUrl}</p>";

        return new EmailMessage(recipientEmail, subject, htmlBody, textBody);
    }
}
