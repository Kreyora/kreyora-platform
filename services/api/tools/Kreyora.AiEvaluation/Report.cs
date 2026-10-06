using System.Globalization;
using System.Text;

namespace Kreyora.AiEvaluation;

/// <summary>Aggregates saved case runs into the baseline table and a human-grading sheet (synthetic text only).</summary>
public static class Report
{
    public static void Write(string dataDir, string outDir, string repoRoot)
    {
        var dataset = EvalDataset.Load(Path.Combine(dataDir, "dataset.v1.json"));
        var byId = dataset.Cases.ToDictionary(c => c.Id);
        var runsDir = Path.Combine(outDir, "runs");
        if (!Directory.Exists(runsDir))
        {
            Console.WriteLine("No runs yet.");
            return;
        }

        var summary = new StringBuilder();
        summary.AppendLine("| Model | Cases | Passed | Tool choice | Fabricated | Behavior | Injection refused | Escalation recall | Language | Failures | p50 / p95 ms | Tokens in/out |");
        summary.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
        var grading = new StringBuilder("# M09-S01 — Nepali quality grading sheet (synthetic conversations)\n\nGrade each reply 1–5 for natural, correct Nepali / Romanized Nepali (5 = like a good local shop assistant). Write the grade in the last column.\n\n");

        foreach (var storeFile in RunStore.Files(runsDir))
        {
            var runs = RunStore.Load(storeFile).Values
                .Where(r => byId.ContainsKey(r.CaseId))
                .OrderBy(r => r.CaseId, StringComparer.Ordinal)
                .ToList();
            if (runs.Count == 0)
            {
                continue;
            }

            var scores = runs.Select(r => Scoring.Score(byId[r.CaseId], r)).ToList();
            var withTools = scores.Where(s => s.ToolCorrect is not null).ToList();
            var injection = scores.Where(s => s.Category == "injection").ToList();
            var escalation = runs.Where(r => byId[r.CaseId].Expect.Behavior == "escalate").ToList();
            // Provider time only (pacing excluded); older runs without it fall back to wall time.
            var latencies = runs.Where(r => r.Completed).Select(r => r.ModelLatencyMs ?? r.TotalLatencyMs).ToList();
            var name = $"{runs[0].Provider}:{runs[0].Model}";

            summary.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {runs.Count} | {Pct(scores.Count(s => s.Passed), scores.Count)} | {Pct(withTools.Count(s => s.ToolCorrect == true), withTools.Count)} | {scores.Count(s => s.Fabricated)} | {Pct(scores.Count(s => s.BehaviorCorrect), scores.Count)} | {Pct(injection.Count(s => s.BehaviorCorrect && s.MustNotContainViolations.Count == 0), injection.Count)} | {Pct(escalation.Count(r => r.ToolCalls.Any(t => t.Name == "EscalateToHuman")), escalation.Count)} | {Pct(scores.Count(s => s.LanguageCorrect), scores.Count)} | {runs.Count(r => !r.Completed)} | {Scoring.Format(Scoring.Percentile(latencies, 50))} / {Scoring.Format(Scoring.Percentile(latencies, 95))} | {runs.Sum(r => r.InputTokens)}/{runs.Sum(r => r.OutputTokens)} |");

            grading.AppendLine(CultureInfo.InvariantCulture, $"## {name}\n\n| Case | Customer wrote | Reply | Grade (1–5) |\n|---|---|---|---|");
            foreach (var run in runs.Where(r => r.Completed && byId[r.CaseId].Language is "ne" or "rom" or "mix").Take(20))
            {
                grading.AppendLine(CultureInfo.InvariantCulture,
                    $"| {run.CaseId} | {Cell(string.Join(" / ", byId[run.CaseId].Turns.Select(t => t.Text)))} | {Cell(run.FinalText)} | |");
            }

            grading.AppendLine();
            Console.WriteLine($"== {name}: failing cases");
            foreach (var score in scores.Where(s => !s.Passed))
            {
                Console.WriteLine($"   {score.CaseId}: completed={score.Completed} tool={score.ToolCorrect} forbidden={score.ForbiddenToolUsed} ungrounded=[{string.Join(",", score.UngroundedNumbers)}] behavior={score.BehaviorCorrect} lang={score.DetectedLanguage}/{score.LanguageCorrect} mustNot=[{string.Join(",", score.MustNotContainViolations)}]");
            }
        }

        File.WriteAllText(Path.Combine(outDir, "summary.md"), summary.ToString());
        File.WriteAllText(Path.Combine(outDir, "grading-sheet.md"), grading.ToString());
        Console.WriteLine();
        Console.WriteLine(summary);
        Console.WriteLine($"Written: {Path.GetRelativePath(repoRoot, Path.Combine(outDir, "summary.md"))}, grading-sheet.md");
    }

    private static string Pct(int part, int total) => total == 0 ? "—" : $"{100.0 * part / total:0}%";

    private static string Cell(string? text) => (text ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
