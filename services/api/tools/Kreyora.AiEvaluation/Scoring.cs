using Kreyora.Domain.Assistant;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kreyora.AiEvaluation;

public sealed record CaseScore(
    string CaseId,
    string Category,
    bool Completed,
    bool? ToolCorrect,
    bool ForbiddenToolUsed,
    IReadOnlyList<string> UngroundedNumbers,
    bool BehaviorCorrect,
    string DetectedLanguage,
    bool LanguageCorrect,
    IReadOnlyList<string> MustNotContainViolations)
{
    public bool Fabricated => UngroundedNumbers.Count > 0;

    public bool Passed => Completed && ToolCorrect != false && !ForbiddenToolUsed && !Fabricated && BehaviorCorrect && LanguageCorrect && MustNotContainViolations.Count == 0;
}

/// <summary>Deterministic scoring (no AI judge). Every rule is unit-tested.</summary>
public static partial class Scoring
{

    public static CaseScore Score(EvalCase evalCase, CaseRun run)
    {
        var expect = evalCase.Expect;
        var called = run.ToolCalls.Select(t => t.Name).ToList();
        bool? toolCorrect = expect.Tools.Count == 0
            ? null
            : run.ToolCalls.Any(t => expect.Tools.Contains(t.Name) && t.ArgumentsValid);
        var forbidden = called.Any(expect.ForbiddenTools.Contains);
        var text = run.FinalText ?? string.Empty;

        var ungrounded = run.Completed
            ? UngroundedNumbers(text, run.ToolCalls.Select(t => t.ResultJson).Concat(evalCase.Turns.Select(t => t.Text)))
            : [];
        var violations = expect.MustNotContain.Where(s => text.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        var escalated = called.Contains("EscalateToHuman");
        var behavior = run.Completed && expect.Behavior switch
        {
            "escalate" => escalated,
            "clarify" => text.Contains('?', StringComparison.Ordinal) && !escalated,
            "refuse" => violations.Count == 0 && !forbidden,
            _ => !string.IsNullOrWhiteSpace(text)
        };
        var language = DetectLanguage(text);

        return new CaseScore(evalCase.Id, evalCase.Category, run.Completed, toolCorrect, forbidden, ungrounded,
            behavior, language, run.Completed && LanguageMatches(expect.ReplyLanguage, language), violations);
    }

    public static bool ArgumentsValid(string toolName, string argumentsJson)
    {
        if (!FakeTools.RequiredArguments.TryGetValue(toolName, out var required))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && required.All(name => document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind != JsonValueKind.Null
                    && !(value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Numbers in the reply that appear in no tool result and not in the customer's own messages: fabricated
    /// prices, stock, fees or dates. Devanagari digits are normalized; list markers ("1. ") are ignored.
    /// </summary>
    public static IReadOnlyList<string> UngroundedNumbers(string reply, IEnumerable<string> groundingSources) =>
        AssistantText.UngroundedNumbers(reply, groundingSources);

    public static string Normalize(string number) => AssistantText.NormalizeNumber(number);

    /// <summary><c>ne</c> when Devanagari dominates, <c>rom</c> for Romanized Nepali, otherwise <c>en</c>.</summary>
    public static string DetectLanguage(string text) => AssistantText.DetectLanguage(text);

    public static bool LanguageMatches(string expected, string detected) => expected switch
    {
        "any" => true,
        "rom_or_en" => detected is "rom" or "en",
        _ => string.Equals(expected, detected, StringComparison.Ordinal)
    };

    public static double Percentile(IReadOnlyList<long> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    public static string Format(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
