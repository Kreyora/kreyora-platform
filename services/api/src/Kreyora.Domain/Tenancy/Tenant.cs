using Kreyora.Domain.Common;

namespace Kreyora.Domain.Tenancy;

public sealed class Tenant : BaseEntity
{
    public const int DisplayNameMaxLength = 160;
    public const int SlugMaxLength = 80;

    private Tenant()
    {
    }

    public string DisplayName { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string NormalizedSlug { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; }
    public OnboardingState OnboardingState { get; private set; }

    public static Tenant Create(string displayName, string slug)
    {
        var tenant = new Tenant();
        tenant.SetDisplayName(displayName);
        tenant.SetSlug(slug);
        tenant.Status = TenantStatus.Active;
        tenant.OnboardingState = OnboardingState.NotStarted;
        return tenant;
    }

    public void SetDisplayName(string displayName)
    {
        var normalized = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("Tenant display name is required.", nameof(displayName))
            : displayName.Trim();

        DisplayName = normalized.Length > DisplayNameMaxLength
            ? throw new ArgumentOutOfRangeException(nameof(displayName), $"Tenant display name cannot exceed {DisplayNameMaxLength} characters.")
            : normalized;
    }

    public void SetSlug(string slug)
    {
        Slug = NormalizeSlug(slug);
        NormalizedSlug = Slug.ToUpperInvariant();
    }

    public void SetStatus(TenantStatus status) => Status = status;

    public void SetOnboardingState(OnboardingState onboardingState) => OnboardingState = onboardingState;

    public static string NormalizeSlug(string slug)
    {
        var normalized = string.IsNullOrWhiteSpace(slug)
            ? throw new ArgumentException("Tenant slug is required.", nameof(slug))
            : slug.Trim().ToLowerInvariant();

        if (normalized.Length is < 3 or > 80 ||
            !System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
        {
            throw new ArgumentException("Tenant slug must contain 3-80 lowercase letters, numbers, and single hyphens.", nameof(slug));
        }

        return normalized;
    }
}
