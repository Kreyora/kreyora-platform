namespace Kreyora.Application.Authentication;

public interface IAuthenticationService
{
    Task<RegistrationResult> RegisterOwnerAsync(RegisterOwnerRequest request, CancellationToken cancellationToken = default);
    Task<SignInResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);
    Task SignOutAsync();
    Task<AuthenticatedUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
    Task<PasswordResetRequestResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task<PasswordResetResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

public sealed record RegisterOwnerRequest(string DisplayName, string Email, string Password, string TenantDisplayName, string TenantSlug);
public sealed record SignInRequest(string Email, string Password);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
public sealed record AuthenticatedUser(string Id, string DisplayName, string Email);
public sealed record RegistrationResult(bool Succeeded, IReadOnlyList<string> Errors);
public sealed record SignInResult(bool Succeeded, bool IsLockedOut);
public sealed record PasswordResetRequestResult(string? DevelopmentToken);
public sealed record PasswordResetResult(bool Succeeded, IReadOnlyList<string> Errors);
