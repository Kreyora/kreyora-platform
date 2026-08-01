using System.Data;
using System.Security.Claims;
using Kreyora.Application.Authentication;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Kreyora.Infrastructure.Authentication;

public sealed class AuthenticationService(
    AppDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment) : IAuthenticationService
{
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

    public async Task<PasswordResetRequestResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null || !environment.IsDevelopment())
        {
            return new PasswordResetRequestResult(null);
        }

        return new PasswordResetRequestResult(await userManager.GeneratePasswordResetTokenAsync(user));
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
}
