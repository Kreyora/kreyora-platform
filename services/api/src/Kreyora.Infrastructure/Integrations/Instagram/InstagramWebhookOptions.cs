namespace Kreyora.Infrastructure.Integrations.Instagram;

/// <summary>
/// App-level webhook secrets for the Instagram channel (Meta configures one callback and verify token per
/// app object). Bound from the environment/user secrets only; never committed. Absence fails webhook
/// validation closed without blocking application boot.
/// </summary>
public sealed class InstagramWebhookOptions
{
    public const string SectionName = "InstagramWebhook";

    /// <summary>Meta App Secret used for X-Hub-Signature-256 HMAC verification. Empty = validation disabled (fail-closed).</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Verify Token entered in the App Dashboard Webhooks product. Empty = challenge disabled (fail-closed).</summary>
    public string VerifyToken { get; set; } = string.Empty;
}
