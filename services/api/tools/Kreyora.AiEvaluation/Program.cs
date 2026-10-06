using System.Globalization;
using System.Text.Json;
using Kreyora.AiEvaluation;
using Kreyora.Application.Ai;
using Kreyora.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// M09-S01 evaluation harness (opt-in; ADR-018). Synthetic data only. Keys come from the shared user-secrets store.
//   dotnet run --project tools/Kreyora.AiEvaluation -- probe
//   dotnet run --project tools/Kreyora.AiEvaluation -- run --set screening --models GoogleAiStudio:gemini-3.5-flash-lite,GoogleAiStudio:gemini-3.5-flash@none
//   dotnet run --project tools/Kreyora.AiEvaluation -- report
var repoRoot = FindRepoRoot();
var dataDir = Path.Combine(repoRoot, "services", "api", "evaluation", "m09");
var outDir = Path.Combine(repoRoot, "artifacts", "evaluations", "M09-S01");

var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(repoRoot, "services", "api", "src", "Kreyora.WebApi", "appsettings.json"), optional: false)
    .AddUserSecrets(typeof(EvalPrompt).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();
var aiOptions = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

var services = new ServiceCollection()
    .AddLogging(builder => builder.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning))
    .AddHttpClient(OpenAiCompatibleChatClient.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan).Services
    .AddHttpClient("public").Services
    .AddSingleton<OpenAiCompatibleChatClient>()
    .BuildServiceProvider();
var transport = services.GetRequiredService<OpenAiCompatibleChatClient>();
var http = services.GetRequiredService<IHttpClientFactory>().CreateClient("public");

var command = args.FirstOrDefault() ?? "help";
var options = ParseOptions(args.Skip(1).ToArray());
var pacer = new Pacer(TimeSpan.FromSeconds(double.Parse(options.GetValueOrDefault("interval", "4.5"), CultureInfo.InvariantCulture)));

switch (command)
{
    case "probe":
        await Probe.RunAsync(aiOptions, transport, http, pacer, outDir, options.GetValueOrDefault("models"), Reasoning(options));
        break;
    case "run":
        await Benchmark.RunAsync(aiOptions, transport, pacer, dataDir, outDir,
            options.GetValueOrDefault("set", "screening"), options.GetValueOrDefault("models") ?? throw new ArgumentException("--models is required"), Reasoning(options));
        break;
    case "raw":
        await RawDiagnostic.RunAsync(aiOptions, http, options["model"], int.Parse(options.GetValueOrDefault("max-tokens", "250"), CultureInfo.InvariantCulture), options.GetValueOrDefault("extra"));
        break;
    case "report":
        Report.Write(dataDir, outDir, repoRoot);
        break;
    default:
        Console.WriteLine("Commands: probe [--models P:m,...] | run --set screening|full --models P:m,... [--interval seconds] | report");
        break;
}

// Global fallback for models without an @effort suffix. Default: send nothing (some models reject the field).
static string? Reasoning(Dictionary<string, string> options) =>
    options.GetValueOrDefault("reasoning", "default") is var value && value == "default" ? null : value;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Run inside the kreyora-platform repository.");
}

static Dictionary<string, string> ParseOptions(string[] items)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < items.Length - 1; i += 2)
    {
        result[items[i].TrimStart('-')] = items[i + 1];
    }

    return result;
}

namespace Kreyora.AiEvaluation
{
    /// <summary>Client-side pacing so free-tier per-minute limits are respected.</summary>
    public sealed class Pacer(TimeSpan interval)
    {
        private DateTimeOffset next = DateTimeOffset.MinValue;

        public async Task WaitAsync()
        {
            var now = DateTimeOffset.UtcNow;
            if (next > now)
            {
                await Task.Delay(next - now);
            }

            next = DateTimeOffset.UtcNow + interval;
        }
    }

    public static class ModelRef
    {
        /// <summary>Parses <c>Provider:model</c> (the model id itself may contain colons, e.g. <c>:free</c>).</summary>
        public static (string Provider, string Model) Parse(string value)
        {
            var (provider, model, _) = ParseWithReasoning(value);
            return (provider, model);
        }

        /// <summary><c>Provider:model[@effort]</c>, e.g. <c>GoogleAiStudio:gemini-3.5-flash@none</c>; no suffix sends no reasoning field.</summary>
        public static (string Provider, string Model, string? ReasoningEffort) ParseWithReasoning(string value)
        {
            var at = value.LastIndexOf('@');
            var effort = at > 0 ? value[(at + 1)..] : null;
            var reference = at > 0 ? value[..at] : value;
            var index = reference.IndexOf(':', StringComparison.Ordinal);
            return index <= 0
                ? throw new ArgumentException($"Expected Provider:model[@effort], got '{value}'.")
                : (reference[..index], reference[(index + 1)..], string.IsNullOrWhiteSpace(effort) ? null : effort);
        }

        public static string Slug(string provider, string model) =>
            $"{provider}__{string.Concat(model.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '_'))}";

        public static AiProviderOptions Provider(AiOptions options, string name) =>
            options.Providers.TryGetValue(name, out var provider) && !string.IsNullOrWhiteSpace(provider.ApiKey)
                ? provider
                : throw new InvalidOperationException($"Provider {name} has no BaseUrl/ApiKey (set Ai:Providers:{name}:ApiKey in user secrets).");
    }

    public static class Benchmark
    {
        public static async Task RunAsync(AiOptions aiOptions, OpenAiCompatibleChatClient transport, Pacer pacer, string dataDir, string outDir, string set, string models, string? reasoningEffort)
        {
            var dataset = EvalDataset.Load(Path.Combine(dataDir, "dataset.v1.json"));
            var tools = new FakeTools(FakeCatalog.Load(Path.Combine(dataDir, "fake-catalog.v1.json")));
            var cases = set == "full" ? dataset.Cases : dataset.Cases.Where(c => c.Screening).ToList();

            foreach (var reference in models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var (providerName, model, effort) = ModelRef.ParseWithReasoning(reference);
                var provider = ModelRef.Provider(aiOptions, providerName);
                var runner = new CaseRunner(transport, tools, dataset.SystemPromptCanary, effort ?? reasoningEffort);
                var store = RunStore.PathFor(Path.Combine(outDir, "runs"), ModelRef.Slug(providerName, model));
                var done = RunStore.Load(store);
                var rateLimitedInARow = 0;
                Console.WriteLine($"== {providerName}:{model} ({set}, {cases.Count} cases)");

                foreach (var evalCase in cases)
                {
                    if (done.TryGetValue(evalCase.Id, out var earlier) && earlier.FailureKind is not ("RateLimited" or "ProviderUnavailable"))
                    {
                        continue; // resumable: done earlier (transient provider failures are retried)
                    }

                    var run = await runner.RunAsync(providerName, provider, model, evalCase, pacer.WaitAsync, CancellationToken.None);
                    if (run.FailureKind == nameof(AiFailureKind.RateLimited))
                    {
                        rateLimitedInARow++;
                        Console.WriteLine($"   {evalCase.Id}: rate limited ({rateLimitedInARow})");
                        if (rateLimitedInARow >= 3)
                        {
                            Console.WriteLine("   Stopping this model: limit reached (likely the daily free cap). Re-run later to resume.");
                            break;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(65));
                        continue;
                    }

                    rateLimitedInARow = 0;
                    RunStore.Append(store, run);
                    var score = Scoring.Score(evalCase, run);
                    Console.WriteLine($"   {evalCase.Id}: {(score.Passed ? "pass" : "FAIL")} {run.TotalLatencyMs} ms {(run.FailureKind ?? string.Empty)}");
                }
            }
        }
    }
}
