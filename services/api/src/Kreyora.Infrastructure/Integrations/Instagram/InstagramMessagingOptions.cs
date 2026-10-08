namespace Kreyora.Infrastructure.Integrations.Instagram;

/// <summary>Instagram messaging policy (ADR-017). Non-secret; safe defaults until Meta approvals exist.</summary>
public sealed class InstagramMessagingOptions
{
    public const string SectionName = "InstagramMessaging";

    /// <summary>
    /// Whether Meta App Review has approved the HUMAN_AGENT tag (7-day manual-response window). Until true,
    /// replies 24h–7d after the customer's last message are refused with a clear reason.
    /// </summary>
    public bool HumanAgentTagApproved { get; set; }

    /// <summary>
    /// Conservative text limit. Meta's Send API documentation (accessed 2026-10-05) states no explicit
    /// maximum; tighten or relax once sandbox behaviour is observed (M08-S07).
    /// </summary>
    public int MaxTextLength { get; set; } = 1000;

    /// <summary>
    /// M09-S08 Q8: look up a new customer's Instagram name and username for the inbox (staff only, never sent to the
    /// assistant). Off by default until verified with real traffic; failures keep the masked label.
    /// </summary>
    public bool ProfileLookupEnabled { get; set; }

    /// <summary>Minimum days between profile lookups for the same customer.</summary>
    public int ProfileRefreshDays { get; set; } = 7;
}
