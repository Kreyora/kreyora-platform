using System.ComponentModel.DataAnnotations;

namespace Kreyora.WebApi.Configuration;

public sealed class AppSettings
{
    public const string SectionName = "App";

    [Required]
    public string Name { get; set; } = "Kreyora API";

    [Required]
    public string Version { get; set; } = "0.1.0";
}

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    public string? ConnectionString { get; set; }
}

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class PublicStorefrontOptions
{
    public const string SectionName = "PublicStorefront";

    [Required]
    [RegularExpression("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$")]
    public string PlatformBaseDomain { get; set; } = string.Empty;

    public bool EnableDevelopmentSlugRoutes { get; set; } = true;

    [Range(1, 3600)]
    public int ReadCacheSeconds { get; set; } = 60;

    [Range(1, 65536)]
    public int WriteBodyLimitBytes { get; set; } = 16 * 1024;

    [Range(1, 10000)]
    public int ReadRequestsPerMinute { get; set; } = 120;

    [Range(1, 10000)]
    public int QuoteRequestsPerTenMinutes { get; set; } = 20;

    [Range(1, 10000)]
    public int SessionRequestsPerTenMinutes { get; set; } = 10;

    [Range(1, 10000)]
    public int OrderRequestsPerHour { get; set; } = 5;

    public string[] TrustedProxyAddresses { get; set; } = [];

    public bool IsValidForEnvironment(bool isDevelopmentOrTesting)
    {
        if (string.IsNullOrWhiteSpace(PlatformBaseDomain)) return false;
        return isDevelopmentOrTesting ||
            (!EnableDevelopmentSlugRoutes && !PlatformBaseDomain.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !PlatformBaseDomain.EndsWith(".test", StringComparison.OrdinalIgnoreCase));
    }
}
