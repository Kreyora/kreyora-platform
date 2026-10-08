using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kreyora.AiEvaluation;

/// <summary>
/// M09-S08 pipeline mode: each case runs through the real assistant turn (owner playground: real prompt, retrieval,
/// registry, validator and budgets; writes dry-run; nothing sent) against the seeded synthetic shop.
/// </summary>
public static class PipelineRunner
{
    public const string RunFile = "pipeline";
    private static readonly string[] InvalidToolOutcomes = ["invalid_arguments", "tool_not_allowed", "repeated_call", "too_many_calls", "unknown_tool"];

    public static async Task RunAsync(ApiSession api, EvalDataset dataset, FakeCatalog catalog, string runsDir, string set, Pacer pacer, Action<string> log, CancellationToken cancellationToken)
    {
        var cases = set == "full" ? dataset.Cases : dataset.Cases.Where(c => c.Screening).ToList();
        var store = RunStore.PathFor(runsDir, RunFile);
        var done = RunStore.Load(store);
        var catalogJson = JsonSerializer.Serialize(catalog, EvalDataset.Json);
        var faq = PipelineSeeder.FaqText(catalog);
        var stoppedForLimits = 0;
        log($"== pipeline ({set}, {cases.Count} cases; {done.Count} already recorded)");

        foreach (var evalCase in cases)
        {
            if (done.TryGetValue(evalCase.Id, out var earlier) && !Retryable(earlier)) continue;

            await pacer.WaitAsync();
            var body = new { messages = evalCase.Turns.Select(t => new { from = t.Role == "assistant" ? "shop" : "customer", text = t.Text }) };
            var stopwatch = Stopwatch.StartNew();
            var (status, json, text) = await api.TrySendAsync(HttpMethod.Post, "v1/assistant/playground", body, cancellationToken);
            stopwatch.Stop();
            if (status == HttpStatusCode.Conflict)
            {
                log($"   {evalCase.Id}: assistant busy, retried later");
                continue;
            }

            if (status != HttpStatusCode.OK || json is null)
            {
                throw new InvalidOperationException($"Playground failed for {evalCase.Id}: {(int)status} {text[..Math.Min(text.Length, 200)]}");
            }

            JsonNode? turn = null;
            if (json["turnId"]?.GetValue<string>() is { } turnId)
            {
                var turns = await api.GetAsync("v1/assistant/turns?pageSize=10", cancellationToken);
                turn = turns?["items"]?.AsArray().FirstOrDefault(t => t?["id"]?.GetValue<string>() == turnId);
            }

            var run = ToCaseRun(evalCase, json, turn, stopwatch.ElapsedMilliseconds, catalogJson, faq);
            if (Retryable(run))
            {
                // Daily free-tier limits end the run for today; the store keeps it resumable.
                log($"   {evalCase.Id}: {run.ReasonCode} (limit)");
                if (++stoppedForLimits >= 3)
                {
                    log("   Stopping: provider or budget limit reached (likely today's free quota). Run again tomorrow to resume.");
                    break;
                }

                continue;
            }

            stoppedForLimits = 0;
            RunStore.Append(store, run);
            var score = Scoring.Score(evalCase, run);
            log($"   {evalCase.Id}: {(score.Passed ? "pass" : "FAIL")} {run.Outcome}/{run.ReasonCode} {run.TotalLatencyMs} ms");
        }
    }

    /// <summary>
    /// Maps one playground result (and its turn-log row) to a scored run. Grounding sources: the shop's data when a
    /// commerce tool ran, the FAQ when knowledge was cited, and always the customer's own messages — so any number the
    /// reply states without a tool counts as fabricated, as in M09-S01.
    /// </summary>
    public static CaseRun ToCaseRun(EvalCase evalCase, JsonNode result, JsonNode? turn, long wallMs, string catalogJson, string faqText)
    {
        var outcome = Pascal(result["outcome"]?.GetValue<string>() ?? "unknown"); // the API writes enums in camelCase
        var reason = result["reasonCode"]?.GetValue<string>() ?? "unknown";
        var reply = result["reply"]?.GetValue<string>();
        var citations = result["citations"]?.AsArray().Count ?? 0;
        var sources = new List<string>();
        var tools = (result["tools"]?.AsArray() ?? []).Select(t =>
        {
            var name = t!["tool"]!.GetValue<string>();
            var toolOutcome = t["outcome"]?.GetValue<string>() ?? "ok";
            var grounding = name == "EscalateToHuman" ? "{}" : catalogJson;
            return new ToolInvocation(name, "{}", grounding, !InvalidToolOutcomes.Contains(toolOutcome));
        }).ToList();
        if (citations > 0) tools.Add(new ToolInvocation("ApprovedKnowledge", "{}", faqText, true));
        if (string.Equals(outcome, "Escalated", StringComparison.OrdinalIgnoreCase) && tools.All(t => t.Name != "EscalateToHuman"))
        {
            tools.Add(new ToolInvocation("EscalateToHuman", "{}", "{}", true)); // pre-check hand-off (keyword / person request)
        }

        var completed = outcome is "Replied" or "Escalated" && !string.IsNullOrWhiteSpace(reply);
        var modelCalls = turn?["modelCalls"]?.AsArray() ?? [];
        var first = modelCalls.FirstOrDefault();
        long? modelLatency = modelCalls.Count == 0 ? null : modelCalls.Sum(c => c?["latencyMs"]?.GetValue<long>() ?? 0);
        return new CaseRun(
            evalCase.Id,
            first?["provider"]?.GetValue<string>() ?? "pipeline",
            first?["model"]?.GetValue<string>() ?? "none",
            completed,
            reply,
            tools,
            result["modelCalls"]?.GetValue<int>() ?? 0,
            wallMs,
            result["inputTokens"]?.GetValue<int>() ?? 0,
            result["outputTokens"]?.GetValue<int>() ?? 0,
            completed ? null : reason,
            turn?["promptVersion"]?.GetValue<string>() ?? "unknown",
            DateTimeOffset.UtcNow,
            modelLatency,
            outcome,
            reason,
            result["validationCodes"]?.AsArray().Select(v => v!.GetValue<string>()).ToList() ?? [],
            result["turnId"]?.GetValue<string>());
    }

    private static string Pascal(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>Provider limits and busy budgets are retried on a later run instead of being scored.</summary>
    public static bool Retryable(CaseRun run) =>
        run.ReasonCode is "circuit_open" or "platform_daily_limit" or "tenant_daily_limit" or "provider_failure:ratelimited" or "provider_failure:providerunavailable";
}
