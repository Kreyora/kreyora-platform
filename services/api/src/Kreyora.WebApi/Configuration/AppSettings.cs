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
