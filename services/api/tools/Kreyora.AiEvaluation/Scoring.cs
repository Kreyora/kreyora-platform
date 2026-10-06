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
    private static readonly string[] RomanizedNepaliWords =
    [
        "cha", "chha", "chaina", "ho", "hoina", "hajur", "kati", "ko", "ma", "huncha", "hunchha", "milcha", "dinus",
        "dinuhos", "garnu", "garna", "garidinus", "parcha", "pugyo", "aayo", "aaucha", "tapai", "tapaiko", "mero", "ra",
        "pani", "ni", "ta", "lai", "sanga", "bhayo", "bata", "samma", "wala", "chahiyo", "dhanyabad", "namaste", "ramro",
        "thik", "kun", "kunai", "huna", "sakchha", "sakcha", "hola", "khusi", "lagcha", "mildaina", "chan", "ki",
        // casual chat spellings seen in M09-S01 replies (e.g. "connect gardai xu hai, ekchhin kurnus na")
        "hajurlai", "xa", "xu", "chu", "hai", "na", "gardai", "garchu", "gardinchu", "ekchhin", "ekchin", "kurnus",
        "kripaya", "dhanyabaad", "dhanyawad", "aba", "aru", "pathaunus", "pathaidinchu", "huncha", "parchha", "bhanus",
        "bhannu", "sakinchha", "ekdam", "maaf", "garnus", "rakhnus", "chaiyo", "chahinchha", "malai", "tapailai"
    ];

    [GeneratedRegex(@"[0-9०-९][0-9०-९,]*(?:\.[0-9०-९]+)?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^\s*(?:[-*]\s*)?\**[0-9०-९]+[.)]\**\s", RegexOptions.Multiline)]
    private static partial Regex ListMarker();

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
    public static IReadOnlyList<string> UngroundedNumbers(string reply, IEnumerable<string> groundingSources)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in groundingSources)
        {
            foreach (Match match in NumberPattern().Matches(source))
            {
                allowed.Add(Normalize(match.Value));
            }
        }

        var cleaned = ListMarker().Replace(reply, " ");
        return NumberPattern().Matches(cleaned)
            .Select(m => Normalize(m.Value))
            .Where(n => n.Length > 0 && !allowed.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static string Normalize(string number)
    {
        var builder = new StringBuilder(number.Length);
        foreach (var ch in number)
        {
            if (ch is >= '०' and <= '९')
            {
                builder.Append((char)('0' + (ch - '०')));
            }
            else if (ch is >= '0' and <= '9' or '.')
            {
                builder.Append(ch);
            }
        }

        var text = builder.ToString().TrimEnd('.');
        return text.EndsWith(".00", StringComparison.Ordinal) ? text[..^3] : text;
    }

    /// <summary><c>ne</c> when Devanagari dominates, <c>rom</c> for Romanized Nepali, otherwise <c>en</c>.</summary>
    public static string DetectLanguage(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0)
        {
            return "unknown";
        }

        var devanagari = letters.Count(c => c is >= 'ऀ' and <= 'ॿ');
        if (devanagari >= letters.Count * 0.3)
        {
            return "ne";
        }

        var words = Regex.Split(text.ToLowerInvariant(), @"[^a-z]+").Where(w => w.Length > 0).ToList();
        var hits = words.Count(w => RomanizedNepaliWords.Contains(w));
        return hits >= Math.Max(2, words.Count / 12) ? "rom" : "en";
    }

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
