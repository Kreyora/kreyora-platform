using System.Net.Mail;
using System.Text.RegularExpressions;
using Kreyora.Domain.Common;

namespace Kreyora.Domain.Storefront;

public sealed class Store : BaseEntity, ITenantOwned
{
    public const int DisplayNameMaxLength = 160;
    public const int PlatformSlugMaxLength = 80;
    public const int TaglineMaxLength = 280;
    public const int ContactValueMaxLength = 320;
    public const int UrlMaxLength = 500;
    public const int PolicyMaxLength = 8_000;

    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex AccentPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new("^\\+?[0-9]{6,24}$", RegexOptions.Compiled);

    private Store()
    {
    }

    public string TenantId { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PlatformSlug { get; private set; } = string.Empty;
    public string NormalizedPlatformSlug { get; private set; } = string.Empty;
    public string? Tagline { get; private set; }
    public StoreStatus Status { get; private set; }
    public StoreThemePreset ThemePreset { get; private set; }
    public string? BrandAccentHex { get; private set; }
    public string? ContactName { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? ContactWhatsApp { get; private set; }
    public string? FacebookUrl { get; private set; }
    public string? InstagramUrl { get; private set; }
    public string? TikTokUrl { get; private set; }
    public string? TermsPolicy { get; private set; }
    public string? PrivacyPolicy { get; private set; }
    public string? ReturnsPolicy { get; private set; }
    public string? PaymentPolicy { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }

    public static Store Create(string tenantId, StoreSettings settings)
    {
        var store = new Store
        {
            TenantId = Require(tenantId, nameof(tenantId), 26),
            Status = StoreStatus.Draft
        };
        store.UpdateSettings(settings);
        return store;
    }

    public void UpdateSettings(StoreSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DisplayName = Require(settings.DisplayName, nameof(settings.DisplayName), DisplayNameMaxLength);
        var slug = NormalizePlatformSlug(settings.PlatformSlug);
        if (Status == StoreStatus.Active && !string.Equals(PlatformSlug, slug, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An active store cannot change its platform slug.");
        }

        PlatformSlug = slug;
        NormalizedPlatformSlug = slug.ToUpperInvariant();
        Tagline = Optional(settings.Tagline, TaglineMaxLength);
        ThemePreset = settings.ThemePreset;
        BrandAccentHex = NormalizeAccent(settings.BrandAccentHex);
        ContactName = Optional(settings.ContactName, ContactValueMaxLength);
        ContactEmail = NormalizeEmail(settings.ContactEmail);
        ContactPhone = NormalizePhone(settings.ContactPhone, nameof(settings.ContactPhone));
        ContactWhatsApp = NormalizePhone(settings.ContactWhatsApp, nameof(settings.ContactWhatsApp));
        FacebookUrl = NormalizeUrl(settings.FacebookUrl, nameof(settings.FacebookUrl));
        InstagramUrl = NormalizeUrl(settings.InstagramUrl, nameof(settings.InstagramUrl));
        TikTokUrl = NormalizeUrl(settings.TikTokUrl, nameof(settings.TikTokUrl));
        TermsPolicy = NormalizePolicy(settings.TermsPolicy, nameof(settings.TermsPolicy));
        PrivacyPolicy = NormalizePolicy(settings.PrivacyPolicy, nameof(settings.PrivacyPolicy));
        ReturnsPolicy = NormalizePolicy(settings.ReturnsPolicy, nameof(settings.ReturnsPolicy));
        PaymentPolicy = NormalizePolicy(settings.PaymentPolicy, nameof(settings.PaymentPolicy));
    }

    public void Activate(DateTimeOffset now)
    {
        if (Status != StoreStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft store can be activated.");
        }

        Status = StoreStatus.Active;
        ActivatedAt = now;
    }

    public static string NormalizePlatformSlug(string slug)
    {
        var normalized = Require(slug, nameof(slug), PlatformSlugMaxLength).ToLowerInvariant();
        if (normalized.Length is < 3 or > PlatformSlugMaxLength || !SlugPattern.IsMatch(normalized))
        {
            throw new ArgumentException("Store slug must contain 3-80 lowercase letters, numbers, and single hyphens.", nameof(slug));
        }

        return normalized;
    }

    private static string? NormalizeAccent(string? accent)
    {
        var normalized = Optional(accent, 7);
        if (normalized is not null && !AccentPattern.IsMatch(normalized))
        {
            throw new ArgumentException("Brand accent must be a six-digit hexadecimal color.", nameof(accent));
        }

        return normalized?.ToUpperInvariant();
    }

    private static string? NormalizeEmail(string? email)
    {
        var normalized = Optional(email, ContactValueMaxLength);
        if (normalized is null) return null;
        try
        {
            var address = new MailAddress(normalized);
            if (!string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase)) throw new FormatException();
            return address.Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            throw new ArgumentException("Store contact email is invalid.", nameof(email));
        }
    }

    private static string? NormalizePhone(string? phone, string parameterName)
    {
        var normalized = Optional(phone, ContactValueMaxLength)?.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized is not null && !PhonePattern.IsMatch(normalized))
        {
            throw new ArgumentException("Store contact phone must contain 6-24 digits and an optional leading plus.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeUrl(string? value, string parameterName)
    {
        var normalized = Optional(value, UrlMaxLength);
        if (normalized is null) return null;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("Store social links must be absolute HTTP(S) URLs.", parameterName);
        }

        return uri.AbsoluteUri;
    }

    private static string? NormalizePolicy(string? value, string parameterName)
    {
        var normalized = Optional(value, PolicyMaxLength);
        if (normalized is not null && (normalized.Contains('<', StringComparison.Ordinal) || normalized.Contains('>', StringComparison.Ordinal)))
        {
            throw new ArgumentException("Store policy content cannot contain HTML markup.", parameterName);
        }

        return normalized;
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.") : normalized;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(nameof(value), $"Value cannot exceed {maximumLength} characters.") : normalized;
    }
}

public sealed record StoreSettings(
    string DisplayName,
    string PlatformSlug,
    string? Tagline,
    StoreThemePreset ThemePreset,
    string? BrandAccentHex,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? ContactWhatsApp,
    string? FacebookUrl,
    string? InstagramUrl,
    string? TikTokUrl,
    string? TermsPolicy,
    string? PrivacyPolicy,
    string? ReturnsPolicy,
    string? PaymentPolicy);
