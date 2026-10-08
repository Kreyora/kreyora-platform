using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kreyora.AiEvaluation;

/// <summary>
/// M09-S08 report: ADR-023 thresholds, per-category results, failing cases, and the owner's grading sheet (written once,
/// so re-running the report never overwrites grades). Synthetic text only.
/// </summary>
public static partial class PipelineReport
{
    public const string GradingSheet = "grading-sheet.md";

    public static void Write(EvalDataset dataset, string outDir, string repoRoot, (decimal, decimal)? pricePerMillion)
    {
        var runs = RunStore.Load(RunStore.PathFor(Path.Combine(outDir, "runs"), PipelineRunner.RunFile)).Values.OrderBy(r => r.CaseId, StringComparer.Ordinal).ToList();
        if (runs.Count == 0)
        {
            Console.WriteLine("No pipeline runs yet.");
            return;
        }

        var byId = dataset.Cases.ToDictionary(c => c.Id);
        var gradingPath = Path.Combine(outDir, GradingSheet);
        if (!File.Exists(gradingPath)) File.WriteAllText(gradingPath, GradingSheetFor(dataset, runs));
        var grades = ReadGrades(File.ReadAllText(gradingPath));
        var thresholds = Thresholds.Evaluate(dataset.Cases, runs, grades, pricePerMillion);

        var md = new StringBuilder();
        md.AppendLine("# M09-S08 evaluation summary (pipeline mode, synthetic shop)").AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"- Cases recorded: {runs.Count} of {dataset.Cases.Count} (dataset v{dataset.Version})");
        md.AppendLine(CultureInfo.InvariantCulture, $"- Models seen: {string.Join(", ", runs.Select(r => $"{r.Provider}:{r.Model}").Distinct(StringComparer.Ordinal))}");
        md.AppendLine(CultureInfo.InvariantCulture, $"- Prompt versions: {string.Join(", ", runs.Select(r => r.PromptVersion).Distinct(StringComparer.Ordinal))}").AppendLine();
        md.AppendLine("## ADR-023 thresholds").AppendLine();
        md.AppendLine("| Metric | Measured | Target | Kind | Result |").AppendLine("|---|---|---|---|---|");
        foreach (var t in thresholds)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {t.Metric} | {t.Value} | {t.Target} | {(t.Hard ? "Hard" : "Soft")} | {(t.Passed switch { true => "✅ pass", false => "❌ fail", null => "⏳ pending" })} |");
        }

        md.AppendLine().AppendLine("## By category").AppendLine().AppendLine("| Category | Cases | Passed |").AppendLine("|---|---:|---:|");
        foreach (var group in runs.Where(r => byId.ContainsKey(r.CaseId)).GroupBy(r => byId[r.CaseId].Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var passed = group.Count(r => Scoring.Score(byId[r.CaseId], r).Passed);
            md.AppendLine(CultureInfo.InvariantCulture, $"| {group.Key} | {group.Count()} | {passed} |");
        }

        md.AppendLine().AppendLine("## Failing cases").AppendLine();
        foreach (var run in runs.Where(r => byId.ContainsKey(r.CaseId)))
        {
            var score = Scoring.Score(byId[run.CaseId], run);
            if (score.Passed) continue;
            md.AppendLine(CultureInfo.InvariantCulture,
                $"- **{run.CaseId}** ({score.Category}): outcome {run.Outcome}/{run.ReasonCode}; completed={score.Completed} tool={score.ToolCorrect?.ToString() ?? "n/a"} forbidden={score.ForbiddenToolUsed} ungrounded=[{string.Join(",", score.UngroundedNumbers)}] behavior={score.BehaviorCorrect} lang={score.DetectedLanguage}/{score.LanguageCorrect} mustNot=[{string.Join(",", score.MustNotContainViolations)}] validation=[{string.Join(",", run.ValidationCodes ?? [])}]");
        }

        File.WriteAllText(Path.Combine(outDir, "summary.md"), md.ToString());
        Console.WriteLine(md);
        Console.WriteLine($"Written: {Path.GetRelativePath(repoRoot, Path.Combine(outDir, "summary.md"))}; grading sheet: {Path.GetRelativePath(repoRoot, gradingPath)} ({grades.Count} grades so far)");
    }

    /// <summary>20 replies across scripts (8 Devanagari, 8 Romanized/mixed, 4 English), by case id.</summary>
    public static string GradingSheetFor(EvalDataset dataset, IReadOnlyList<CaseRun> runs)
    {
        var byId = dataset.Cases.ToDictionary(c => c.Id);
        var done = runs.Where(r => r.Completed && byId.ContainsKey(r.CaseId)).OrderBy(r => r.CaseId, StringComparer.Ordinal).ToList();
        var sample = done.Where(r => byId[r.CaseId].Language == "ne").Take(8)
            .Concat(done.Where(r => byId[r.CaseId].Language is "rom" or "mix").Take(8))
            .Concat(done.Where(r => byId[r.CaseId].Language == "en").Take(4)).ToList();
        var sheet = new StringBuilder("# M09-S08 — owner grading sheet (synthetic conversations)\n\n");
        sheet.AppendLine("Grade each reply 1–5 for natural, correct, helpful wording in the customer's language (5 = like a good local shop assistant). Put the number in the last column. Re-running the report keeps your grades.\n");
        sheet.AppendLine("| Case | Customer wrote | Reply | Grade (1–5) |").AppendLine("|---|---|---|---|");
        foreach (var run in sample)
        {
            sheet.AppendLine(CultureInfo.InvariantCulture, $"| {run.CaseId} | {Cell(string.Join(" / ", byId[run.CaseId].Turns.Select(t => t.Text)))} | {Cell(run.FinalText)} | |");
        }

        return sheet.ToString();
    }

    /// <summary>Grades are the last cell of each table row when it holds 1–5.</summary>
    public static IReadOnlyList<int> ReadGrades(string sheet) =>
        [.. sheet.Split('\n').Select(line => GradeCell().Match(line)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))];

    [GeneratedRegex(@"^\|.*\|\s*([1-5])\s*\|\s*$")]
    private static partial Regex GradeCell();

    private static string Cell(string? text) => (text ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
