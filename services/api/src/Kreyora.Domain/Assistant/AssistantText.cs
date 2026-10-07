using System.Text;
using System.Text.RegularExpressions;

namespace Kreyora.Domain.Assistant;

/// <summary>
/// Text rules shared by the live output validator (M09-S06) and the evaluation harness (M09-S01): grounding of numbers,
/// reply language, links, card/OTP-like digits and markdown. Pure functions; never log their inputs.
/// </summary>
public static partial class AssistantText
{
    private static readonly HashSet<string> RomanizedNepaliWords = new(StringComparer.Ordinal)
    {
        "cha", "chha", "chaina", "ho", "hoina", "hajur", "kati", "ko", "ma", "huncha", "hunchha", "milcha", "dinus",
        "dinuhos", "garnu", "garna", "garidinus", "parcha", "pugyo", "aayo", "aaucha", "tapai", "tapaiko", "mero", "ra",
        "pani", "ni", "ta", "lai", "sanga", "bhayo", "bata", "samma", "wala", "chahiyo", "dhanyabad", "namaste", "ramro",
        "thik", "kun", "kunai", "huna", "sakchha", "sakcha", "hola", "khusi", "lagcha", "mildaina", "chan", "ki",
        // casual chat spellings seen in M09-S01 replies (e.g. "connect gardai xu hai, ekchhin kurnus na")
        "hajurlai", "xa", "xu", "chu", "hai", "na", "gardai", "garchu", "gardinchu", "ekchhin", "ekchin", "kurnus",
        "kripaya", "dhanyabaad", "dhanyawad", "aba", "aru", "pathaunus", "pathaidinchu", "parchha", "bhanus",
        "bhannu", "sakinchha", "ekdam", "maaf", "garnus", "rakhnus", "chaiyo", "chahinchha", "malai", "tapailai"
    };

    [GeneratedRegex(@"[0-9०-९][0-9०-९,]*(?:\.[0-9०-९]+)?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^\s*(?:[-*]\s*)?\**[0-9०-९]+[.)]\**\s", RegexOptions.Multiline)]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>""')\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    // 12-19 digits, optionally grouped by spaces or dashes: card or account numbers.
    [GeneratedRegex(@"(?<![0-9])(?:[0-9][ -]?){11,18}[0-9](?![0-9])")]
    private static partial Regex LongDigitRun();

    // A code within two words after OTP/PIN/CVV/password, e.g. "my OTP is 482913".
    [GeneratedRegex(@"\b(?:otp|pin|cvv|password|passcode|verification code)\b(?:\W+[a-z]+){0,2}\W+[0-9]{3,8}\b", RegexOptions.IgnoreCase)]
    private static partial Regex SecretCode();

    [GeneratedRegex(@"\*\*|__|`+|^#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex MarkdownMarks();

    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex MarkdownLink();

    /// <summary>Numbers in <paramref name="reply"/> found in none of the sources (fabricated prices, fees, dates…). List markers ("1. ") are ignored.</summary>
    public static IReadOnlyList<string> UngroundedNumbers(string reply, IEnumerable<string> groundingSources)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(groundingSources);
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in groundingSources)
        {
            foreach (Match match in NumberPattern().Matches(source ?? string.Empty))
            {
                allowed.Add(NormalizeNumber(match.Value));
            }
        }

        var cleaned = ListMarker().Replace(reply, " ");
        return [.. NumberPattern().Matches(cleaned).Select(m => NormalizeNumber(m.Value)).Where(n => n.Length > 0 && !allowed.Contains(n)).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Devanagari digits → ASCII, thousands separators dropped, trailing ".00" removed.</summary>
    public static string NormalizeNumber(string number)
    {
        ArgumentNullException.ThrowIfNull(number);
        var builder = new StringBuilder(number.Length);
        foreach (var ch in number)
        {
            if (ch is >= '०' and <= '९') builder.Append((char)('0' + (ch - '०')));
            else if (ch is >= '0' and <= '9' or '.') builder.Append(ch);
        }

        var text = builder.ToString().TrimEnd('.');
        return text.EndsWith(".00", StringComparison.Ordinal) ? text[..^3] : text;
    }

    /// <summary><c>ne</c> when Devanagari dominates, <c>rom</c> for Romanized Nepali, <c>en</c> otherwise, <c>unknown</c> without letters.</summary>
    public static string DetectLanguage(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return "unknown";
        var devanagari = letters.Count(c => c is >= 'ऀ' and <= 'ॿ');
        if (devanagari >= letters.Count * 0.3) return "ne";
        var words = Regex.Split(text.ToLowerInvariant(), "[^a-z]+").Where(w => w.Length > 0).ToList();
        var hits = words.Count(RomanizedNepaliWords.Contains);
        return hits >= Math.Max(2, words.Count / 12) ? "rom" : "en";
    }

    public static IReadOnlyList<string> Links(string text) =>
        [.. LinkPattern().Matches(text ?? string.Empty).Select(m => m.Value.TrimEnd('.', ',', '!', '?', ';', ':'))];

    /// <summary>True when the text contains card/account-like digit runs or a code next to OTP/PIN/CVV/password.</summary>
    public static bool ContainsSecretLikeDigits(string text) =>
        !string.IsNullOrEmpty(text) && (LongDigitRun().IsMatch(text) || SecretCode().IsMatch(text));

    /// <summary>Plain text for chat channels: bold/code/heading marks removed, markdown links become "label url".</summary>
    public static string StripMarkdown(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var withoutLinks = MarkdownLink().Replace(text, "$1 $2");
        return MarkdownMarks().Replace(withoutLinks, string.Empty).Trim();
    }
}
