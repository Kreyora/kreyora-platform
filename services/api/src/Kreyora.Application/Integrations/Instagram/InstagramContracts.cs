using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations.Instagram;

/// <summary>
/// Contract models for the proposed Instagram Messaging first-channel adapter (ADR-014, Proposed).
/// Models only — no network calls, no secrets. Credential values are never stored here;
/// connection secrets remain AES-256-GCM <see cref="EncryptedSecret"/> envelopes per ADR-013.
/// Evidence: docs/architecture/PROVIDER_READINESS_EVALUATION.md (accessed 2026-09-22).
/// </summary>
public static class InstagramEvidence
{
    public const string SendApiDocs =
        "https://developers.facebook.com/documentation/business-messaging/messenger-platform/send-messages";

    public const string WebhooksDocs =
        "https://developers.facebook.com/documentation/business-messaging/instagram-messaging/webhooks";

    public const string GettingStartedDocs =
        "https://developers.facebook.com/documentation/business-messaging/instagram-messaging/get-started";

    public const string HumanAgentDocs =
        "https://developers.facebook.com/docs/features-reference/human-agent";

    /// <summary>Standard messaging window: 24 hours from the last customer action.</summary>
    public static readonly TimeSpan StandardMessagingWindow = TimeSpan.FromHours(24);

    /// <summary>HUMAN_AGENT manual-response extension: 7 days from the customer message.</summary>
    public static readonly TimeSpan HumanAgentExtensionWindow = TimeSpan.FromDays(7);
}

/// <summary>Permissions an Instagram connection must hold (never the tokens themselves).</summary>
public sealed record InstagramPermissionSet(
    bool InstagramBasic,
    bool InstagramManageMessages,
    bool PagesManageMetadata)
{
    public static InstagramPermissionSet Required() => new(
        InstagramBasic: true,
        InstagramManageMessages: true,
        PagesManageMetadata: true);

    public bool IsComplete => InstagramBasic && InstagramManageMessages && PagesManageMetadata;
}

/// <summary>Non-secret descriptor of an Instagram connection's credential posture.</summary>
public sealed record InstagramCredentialDescriptor(
    string PageId,
    string InstagramAccountId,
    InstagramPermissionSet Permissions,
    bool UsesLongLivedPageToken,
    bool HasKeyVersion)
{
    public bool IsReadyForMessaging => Permissions.IsComplete && HasKeyVersion;
}

/// <summary>Evaluated outbound-send eligibility for one conversation at one point in time.</summary>
public enum InstagramWindowState
{
    Unknown = 0,
    WindowOpen = 1,
    WindowExpiredHumanAgentEligible = 2,
    WindowExhausted = 3
}

/// <summary>
/// Pure evaluator mapping (last customer action, now) to <see cref="InstagramWindowState"/>.
/// Thresholds from <see cref="InstagramEvidence"/>; no I/O, no secrets.
/// </summary>
public static class InstagramWindowEvaluator
{
    public static InstagramWindowState Evaluate(
        DateTimeOffset? lastCustomerActionAt,
        DateTimeOffset now)
    {
        if (lastCustomerActionAt is null)
        {
            return InstagramWindowState.Unknown;
        }

        var elapsed = now - lastCustomerActionAt.Value;
        if (elapsed < TimeSpan.Zero)
        {
            return InstagramWindowState.Unknown;
        }

        if (elapsed <= InstagramEvidence.StandardMessagingWindow)
        {
            return InstagramWindowState.WindowOpen;
        }

        if (elapsed <= InstagramEvidence.HumanAgentExtensionWindow)
        {
            return InstagramWindowState.WindowExpiredHumanAgentEligible;
        }

        return InstagramWindowState.WindowExhausted;
    }
}

/// <summary>Documented Instagram capability restrictions with operator-facing fallback guidance.</summary>
public sealed record InstagramCapabilityRestriction(
    string Capability,
    string Restriction,
    string FallbackUx,
    string Evidence)
{
    public static IReadOnlyList<InstagramCapabilityRestriction> Documented() => new List<InstagramCapabilityRestriction>
    {
        new(
            Capability: "Delivery receipts",
            Restriction: "Not emitted; only messaging_seen (read) is delivered.",
            FallbackUx: "Advance message state Sent -> Read directly; never render an intermediate Delivered state.",
            Evidence: InstagramEvidence.SendApiDocs + "#delivery_status"),
        new(
            Capability: "GIF / sticker inbound",
            Restriction: "No webhook is triggered for inbound GIF/sticker messages.",
            FallbackUx: "Document as silent limitation; do not surface a sync error.",
            Evidence: InstagramEvidence.WebhooksDocs + "#webhook-events"),
        new(
            Capability: "Disappearing (ephemeral) media",
            Restriction: "Unsupported on media webhooks; no URL is delivered.",
            FallbackUx: "Document as silent limitation; do not surface a sync error.",
            Evidence: InstagramEvidence.WebhooksDocs + "#webhook-events"),
        new(
            Capability: "One-time notifications / sponsored messages",
            Restriction: "Not available for the Instagram Messaging API.",
            FallbackUx: "Disable re-engagement actions outside the window with an explanatory denial reason.",
            Evidence: InstagramEvidence.SendApiDocs + "#more-message-types"),
    };
}
