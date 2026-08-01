using Kreyora.Domain.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Kreyora.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<string>
{
    public const int IdLength = 26;
    public const int DisplayNameMaxLength = 160;

    public ApplicationUser()
    {
        Id = IdGenerator.NewId();
    }

    private string displayName = string.Empty;

    public string DisplayName
    {
        get => displayName;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A display name is required.", nameof(value))
                : value.Trim();

            displayName = normalized.Length > DisplayNameMaxLength
                ? throw new ArgumentOutOfRangeException(nameof(value), $"A display name cannot exceed {DisplayNameMaxLength} characters.")
                : normalized;
        }
    }
}
