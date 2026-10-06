using System.Text.Json;

namespace Kreyora.AiEvaluation;

/// <summary>
/// One JSON-lines file per model (<c>runs/&lt;provider__model&gt;.jsonl</c>); the latest line per case wins, so runs stay
/// resumable without one file per case. Older per-case folders are migrated on first use.
/// </summary>
public static class RunStore
{
    private static readonly JsonSerializerOptions Line = new(EvalDataset.Json) { WriteIndented = false };

    public static string PathFor(string runsDir, string slug) => Path.Combine(runsDir, $"{slug}.jsonl");

    public static Dictionary<string, CaseRun> Load(string file)
    {
        MigrateLegacyFolder(file);
        var runs = new Dictionary<string, CaseRun>(StringComparer.Ordinal);
        if (!File.Exists(file))
        {
            return runs;
        }

        foreach (var line in File.ReadLines(file).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var run = JsonSerializer.Deserialize<CaseRun>(line, Line)!;
            runs[run.CaseId] = run;
        }

        return runs;
    }

    public static void Append(string file, CaseRun run)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.AppendAllText(file, JsonSerializer.Serialize(run, Line) + Environment.NewLine);
    }

    public static IEnumerable<string> Files(string runsDir)
    {
        if (!Directory.Exists(runsDir))
        {
            return [];
        }

        foreach (var legacy in Directory.GetDirectories(runsDir))
        {
            MigrateLegacyFolder(legacy + ".jsonl");
        }

        return Directory.GetFiles(runsDir, "*.jsonl").Order(StringComparer.Ordinal);
    }

    private static void MigrateLegacyFolder(string file)
    {
        var folder = file[..^".jsonl".Length];
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var caseFile in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            Append(file, JsonSerializer.Deserialize<CaseRun>(File.ReadAllText(caseFile), EvalDataset.Json)!);
        }

        Directory.Delete(folder, recursive: true);
    }
}
