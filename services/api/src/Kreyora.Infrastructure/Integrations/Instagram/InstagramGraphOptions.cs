namespace Kreyora.Infrastructure.Integrations.Instagram;

/// <summary>Typed configuration for the Instagram Graph API client. Validated at startup.</summary>
public sealed class InstagramGraphOptions
{
    public const string SectionName = "InstagramGraph";

    public string BaseAddress { get; set; } = "https://graph.facebook.com";

    /// <summary>Graph API version. v21.0 proven against the owner Dev-mode sandbox (M08-S01).</summary>
    public string ApiVersion { get; set; } = "v21.0";

    public int TimeoutSeconds { get; set; } = 15;
}
