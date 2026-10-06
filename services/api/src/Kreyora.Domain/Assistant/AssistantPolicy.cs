using Kreyora.Domain.Common;

namespace Kreyora.Domain.Assistant;

public enum AssistantReplyStyle
{
    MatchCustomer = 1,
    AlwaysRomanized = 2,
    AlwaysDevanagari = 3,
    AlwaysEnglish = 4
}

public enum AssistantTone
{
    Friendly = 1,
    Formal = 2
}

public enum OutsideHoursBehavior
{
    AnswerNormally = 1,
    AnswerAndPromiseFollowUp = 2,
    DoNotAnswer = 3
}

public enum UnrecognizedMediaBehavior
{
    /// <summary>Ask for the product name, or to share the shop's post. Never guess.</summary>
    AskForDetails = 1,
    HandToPerson = 2
}

/// <summary>Opening hours for one weekday in the shop's time zone ("HH:mm"); closed days have no times.</summary>
public sealed record DailyHours(DayOfWeek Day, bool Closed, string? Opens, string? Closes);

/// <summary>
/// A workspace's assistant settings (M09-S02). Created with safe defaults; the seller may change them within
/// platform caps. Effective activation also needs readiness and the platform kill switch (ADR-018 §4).
/// </summary>
public sealed class AssistantPolicy : BaseEntity, ITenantOwned
{
    public const string TimeZoneId = "Asia/Kathmandu";
    public const int BrandNoteMaxLength = 300;
    public const int EscalationKeywordMaxCount = 20;
    public const int EscalationKeywordMaxLength = 40;

    public static readonly IReadOnlyList<string> KnownLanguages = ["ne", "ne-Latn", "en"];

    /// <summary>Read tools available to the assistant (M09-S04). <c>EscalateToHuman</c> is always allowed.</summary>
    public static readonly IReadOnlyList<string> ReadTools = ["SearchProducts", "CheckInventory", "GetPrice", "GetShippingInfo", "GetOrderStatus"];

    /// <summary>Write tools arrive in M09-S05; they cannot be enabled before then.</summary>
    public static readonly IReadOnlyList<string> WriteTools = ["QuoteCart", "CreateOrderDraft", "ReserveInventory", "ReleaseReservation", "CreateCheckoutLink"];

    public const string AlwaysAllowedTool = "EscalateToHuman";

    /// <summary>Situations that always go to a person. Not configurable (safe defaults, M09-S02 plan §A).</summary>
    public static readonly IReadOnlyList<string> FixedEscalationCategories =
    [
        "customer_requests_person", "complaint", "refund_or_exchange", "custom_or_wholesale_order",
        "health_or_safety", "legal", "payment_dispute", "abusive_message"
    ];

    public static readonly IReadOnlyList<DailyHours> DefaultHours =
        Enum.GetValues<DayOfWeek>().Select(day => new DailyHours(day, false, "09:00", "19:00")).ToList();

    private AssistantPolicy() { }

    public string TenantId { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public AssistantReplyStyle ReplyStyle { get; private set; }
    public List<string> SupportedLanguages { get; private set; } = [];
    public AssistantTone Tone { get; private set; }
    public string? BrandNote { get; private set; }
    public List<DailyHours> BusinessHours { get; private set; } = [];
    public OutsideHoursBehavior OutsideHoursBehavior { get; private set; }
    public UnrecognizedMediaBehavior UnrecognizedMediaBehavior { get; private set; }
    public List<string> EscalationKeywords { get; private set; } = [];
    public List<string> AllowedTools { get; private set; } = [];
    public int MaxToolSteps { get; private set; }
    public int MaxRepliesPerConversationPerHour { get; private set; }
    public int MaxOutputTokens { get; private set; }

    /// <summary>Set when the owner first saves the policy; activation requires it (plan Q8).</summary>
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewedByUserId { get; private set; }

    public static AssistantPolicy CreateDefault(string tenantId) => new()
    {
        TenantId = string.IsNullOrWhiteSpace(tenantId) ? throw new ArgumentException("A tenant is required.", nameof(tenantId)) : tenantId,
        Enabled = true, // owner preference: on automatically once ready (plan Q1)
        ReplyStyle = AssistantReplyStyle.MatchCustomer,
        SupportedLanguages = [.. KnownLanguages],
        Tone = AssistantTone.Friendly,
        BusinessHours = [.. DefaultHours],
        OutsideHoursBehavior = OutsideHoursBehavior.AnswerAndPromiseFollowUp,
        UnrecognizedMediaBehavior = UnrecognizedMediaBehavior.AskForDetails,
        AllowedTools = [.. ReadTools, AlwaysAllowedTool],
        MaxToolSteps = 4,
        MaxRepliesPerConversationPerHour = 20,
        MaxOutputTokens = 600
    };

    /// <summary>Applies an already-validated update (see <see cref="AssistantPolicyRules"/>) and marks the policy reviewed.</summary>
    public void Apply(AssistantPolicySettings settings, string reviewedByUserId, DateTimeOffset now)
    {
        Enabled = settings.Enabled;
        ReplyStyle = settings.ReplyStyle;
        SupportedLanguages = [.. settings.SupportedLanguages.Distinct(StringComparer.Ordinal)];
        Tone = settings.Tone;
        BrandNote = string.IsNullOrWhiteSpace(settings.BrandNote) ? null : settings.BrandNote.Trim();
        BusinessHours = [.. settings.BusinessHours.OrderBy(h => h.Day)];
        OutsideHoursBehavior = settings.OutsideHoursBehavior;
        UnrecognizedMediaBehavior = settings.UnrecognizedMediaBehavior;
        EscalationKeywords = [.. settings.EscalationKeywords.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        AllowedTools = [.. settings.AllowedTools.Append(AlwaysAllowedTool).Distinct(StringComparer.Ordinal)];
        MaxToolSteps = settings.MaxToolSteps;
        MaxRepliesPerConversationPerHour = settings.MaxRepliesPerConversationPerHour;
        MaxOutputTokens = settings.MaxOutputTokens;
        ReviewedAt ??= now;
        ReviewedByUserId ??= reviewedByUserId;
    }
}

/// <summary>The editable part of a policy, as submitted by the seller.</summary>
public sealed record AssistantPolicySettings(
    bool Enabled,
    AssistantReplyStyle ReplyStyle,
    IReadOnlyList<string> SupportedLanguages,
    AssistantTone Tone,
    string? BrandNote,
    IReadOnlyList<DailyHours> BusinessHours,
    OutsideHoursBehavior OutsideHoursBehavior,
    UnrecognizedMediaBehavior UnrecognizedMediaBehavior,
    IReadOnlyList<string> EscalationKeywords,
    IReadOnlyList<string> AllowedTools,
    int MaxToolSteps,
    int MaxRepliesPerConversationPerHour,
    int MaxOutputTokens);

/// <summary>Platform maximums the seller's budgets must stay within (from configuration).</summary>
public sealed record AssistantPlatformCaps(int MaxToolSteps, int MaxRepliesPerConversationPerHour, int MaxOutputTokens);

public static class AssistantPolicyRules
{
    /// <summary>Validation errors keyed by field; empty when the settings are acceptable.</summary>
    public static Dictionary<string, string[]> Validate(AssistantPolicySettings s, AssistantPlatformCaps caps)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string field, string message) => errors[field] = errors.TryGetValue(field, out var existing) ? [.. existing, message] : [message];

        if (!Enum.IsDefined(s.ReplyStyle)) Add("replyStyle", "Unknown reply style.");
        if (!Enum.IsDefined(s.Tone)) Add("tone", "Unknown tone.");
        if (!Enum.IsDefined(s.OutsideHoursBehavior)) Add("outsideHoursBehavior", "Unknown outside-hours behavior.");
        if (!Enum.IsDefined(s.UnrecognizedMediaBehavior)) Add("unrecognizedMediaBehavior", "Unknown setting for unidentified photos or links.");

        if (s.SupportedLanguages.Count == 0) Add("supportedLanguages", "Choose at least one language.");
        foreach (var language in s.SupportedLanguages.Where(l => !AssistantPolicy.KnownLanguages.Contains(l)))
        {
            Add("supportedLanguages", $"Unsupported language '{language}'.");
        }

        if (s.BrandNote is { Length: > AssistantPolicy.BrandNoteMaxLength }) Add("brandNote", $"Keep the brand note under {AssistantPolicy.BrandNoteMaxLength} characters.");
        if (s.BrandNote is not null && (s.BrandNote.Contains('<', StringComparison.Ordinal) || s.BrandNote.Contains('>', StringComparison.Ordinal)))
        {
            Add("brandNote", "The brand note cannot contain markup.");
        }

        if (s.BusinessHours.Count != 7 || s.BusinessHours.Select(h => h.Day).Distinct().Count() != 7)
        {
            Add("businessHours", "Provide opening hours for each day of the week exactly once.");
        }

        foreach (var hours in s.BusinessHours.Where(h => !h.Closed))
        {
            if (!TryParseTime(hours.Opens, out var opens) || !TryParseTime(hours.Closes, out var closes) || closes <= opens)
            {
                Add("businessHours", $"{hours.Day}: opening time must be before closing time (HH:mm).");
            }
        }

        if (s.EscalationKeywords.Count > AssistantPolicy.EscalationKeywordMaxCount) Add("escalationKeywords", $"Use at most {AssistantPolicy.EscalationKeywordMaxCount} keywords.");
        if (s.EscalationKeywords.Any(k => k.Trim().Length > AssistantPolicy.EscalationKeywordMaxLength)) Add("escalationKeywords", $"Keywords are limited to {AssistantPolicy.EscalationKeywordMaxLength} characters.");

        foreach (var tool in s.AllowedTools.Where(t => t != AssistantPolicy.AlwaysAllowedTool))
        {
            if (AssistantPolicy.WriteTools.Contains(tool)) Add("allowedTools", $"'{tool}' is not available yet (write tools arrive in M09-S05).");
            else if (!AssistantPolicy.ReadTools.Contains(tool)) Add("allowedTools", $"Unknown tool '{tool}'.");
        }

        if (s.MaxToolSteps < 1 || s.MaxToolSteps > caps.MaxToolSteps) Add("maxToolSteps", $"Must be between 1 and {caps.MaxToolSteps}.");
        if (s.MaxRepliesPerConversationPerHour < 1 || s.MaxRepliesPerConversationPerHour > caps.MaxRepliesPerConversationPerHour)
        {
            Add("maxRepliesPerConversationPerHour", $"Must be between 1 and {caps.MaxRepliesPerConversationPerHour}.");
        }

        if (s.MaxOutputTokens < 50 || s.MaxOutputTokens > caps.MaxOutputTokens) Add("maxOutputTokens", $"Must be between 50 and {caps.MaxOutputTokens}.");
        return errors;
    }

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out time);
}
