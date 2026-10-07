using Kreyora.Domain.Assistant;

namespace Kreyora.Infrastructure.Assistant.Orchestration;

/// <summary>What a final reply is checked against before it may be enqueued (M09-S06 Q4).</summary>
public sealed record OutputRules(
    IReadOnlyList<string> GroundingSources,
    IReadOnlyCollection<string> AllowedLinks,
    int MaxCharacters,
    AssistantReplyStyle ReplyStyle,
    string CustomerLanguage,
    string Canary);

public sealed record OutputCheck(string Text, IReadOnlyList<string> Violations)
{
    public bool IsValid => Violations.Count == 0;
}

/// <summary>
/// Content rules for the assistant's final reply. Pure and deterministic; returns rule codes (logged) and never content.
/// Ownership (takeover, newer customer message) is checked separately against the database right before enqueueing.
/// </summary>
public static class AssistantOutputValidator
{
    public const string Empty = "empty";
    public const string TooLong = "too_long";
    public const string UngroundedNumber = "ungrounded_number";
    public const string ForeignLink = "foreign_link";
    public const string PromptLeak = "prompt_leak";
    public const string SecretLikeDigits = "secret_like_digits";
    public const string WrongLanguage = "wrong_language";
    public const string ToolMarkup = "tool_markup";

    private static readonly string[] ToolMarkers = ["\"ok\":", "\"tool\":", "<tool", "tool_call", "function_call", "```", "{\"data\""];

    public static OutputCheck Validate(string? reply, OutputRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var raw = reply ?? string.Empty;
        var violations = new List<string>();
        if (ToolMarkers.Any(m => raw.Contains(m, StringComparison.OrdinalIgnoreCase))) violations.Add(ToolMarkup);

        var text = AssistantText.StripMarkdown(raw);
        if (string.IsNullOrWhiteSpace(text)) return new OutputCheck(string.Empty, [Empty]);
        if (text.Length > rules.MaxCharacters) violations.Add(TooLong);
        if (AssistantText.UngroundedNumbers(text, rules.GroundingSources).Count > 0) violations.Add(UngroundedNumber);
        if (AssistantText.Links(text).Any(link => !rules.AllowedLinks.Any(allowed => string.Equals(allowed, link, StringComparison.Ordinal))))
            violations.Add(ForeignLink);
        if (rules.Canary.Length > 0 && text.Contains(rules.Canary, StringComparison.OrdinalIgnoreCase)) violations.Add(PromptLeak);
        if (AssistantText.ContainsSecretLikeDigits(text)) violations.Add(SecretLikeDigits);
        if (!LanguageAcceptable(rules.ReplyStyle, rules.CustomerLanguage, AssistantText.DetectLanguage(text))) violations.Add(WrongLanguage);
        return new OutputCheck(text, violations.Distinct().ToList());
    }

    /// <summary>
    /// Script check. The detector can't always tell short English from Romanized Nepali, so those two are accepted for
    /// each other; Devanagari is enforced both ways.
    /// </summary>
    public static bool LanguageAcceptable(AssistantReplyStyle style, string customerLanguage, string replyLanguage)
    {
        if (replyLanguage == "unknown") return true;
        var expected = style switch
        {
            AssistantReplyStyle.AlwaysDevanagari => "ne",
            AssistantReplyStyle.AlwaysEnglish or AssistantReplyStyle.AlwaysRomanized => "latin",
            _ => customerLanguage switch { "ne" => "ne", "rom" or "en" => "latin", _ => "any" }
        };
        return expected switch
        {
            "ne" => replyLanguage == "ne",
            "latin" => replyLanguage is "en" or "rom",
            _ => true
        };
    }
}
