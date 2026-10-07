using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Assistant;
using Kreyora.Infrastructure.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant.Tools;

/// <summary>
/// Versioned, allowlisted tool registry (M09-S04 read tools, M09-S05 write tools, ADR-020). A call runs only if the
/// tool is registered and allowed by the context. Write tools additionally need a conversation (seller preview runs
/// them as a dry run), refuse after a human takeover, and replay their first result for duplicate calls in the same
/// turn. Arguments are strictly validated; each run gets its own DI scope in the context's tenant and a deadline.
/// Every outcome is a JSON envelope for the model plus a values-free trace. Nothing throws.
/// </summary>
public sealed partial class AssistantToolRegistry(
    IEnumerable<IAssistantTool> tools,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AiOptions> aiOptions,
    ITimeProvider timeProvider,
    ILogger<AssistantToolRegistry> logger) : IAssistantToolRegistry
{
    public const string RegistryVersion = "kreyora-tools.v2";

    public static readonly JsonSerializerOptions ResultJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    /// <summary>Every tool the registry knows, in the order they are described and offered.</summary>
    public static readonly IReadOnlyList<string> KnownTools = [.. AssistantPolicy.ReadTools, .. AssistantPolicy.WriteTools, AssistantPolicy.AlwaysAllowedTool];

    private readonly Dictionary<string, IAssistantTool> registered = tools
        .Where(t => KnownTools.Contains(t.Name))
        .ToDictionary(t => t.Name, StringComparer.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Assistant tool {Tool} v{ToolVersion} → {Outcome} in {DurationMs} ms (fields {ArgumentFields}, args {ArgumentsHash}, results {ResultCount}, preview {SellerPreview}, replayed {Replayed})")]
    private static partial void LogTrace(ILogger logger, string tool, int toolVersion, string outcome, long durationMs, string argumentFields, string argumentsHash, int resultCount, bool sellerPreview, bool replayed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant tool {Tool} failed unexpectedly")]
    private static partial void LogFailure(ILogger logger, Exception ex, string tool);

    public string Version => RegistryVersion;

    public static bool IsWriteTool(string name) => AssistantPolicy.WriteTools.Contains(name) || name == AssistantPolicy.AlwaysAllowedTool;

    public IReadOnlyList<AssistantToolDescriptor> Describe(IReadOnlyList<string> enabledTools) =>
        [.. KnownTools.Where(registered.ContainsKey).Select(name => registered[name]).Select(tool =>
            new AssistantToolDescriptor(tool.Name, tool.Version, tool.Description, JsonDocument.Parse(tool.ParametersSchema).RootElement.Clone(),
                tool.Name == AssistantPolicy.AlwaysAllowedTool || enabledTools.Contains(tool.Name)))];

    public IReadOnlyList<AiToolDefinition> GetDefinitions(AssistantToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var writesUsable = context.IsSellerPreview || context.ConversationId is not null && context.IsAutomationActive;
        return [.. KnownTools.Where(name => registered.ContainsKey(name) && context.AllowedTools.Contains(name) && (!IsWriteTool(name) || writesUsable))
            .Select(name => registered[name]).Select(tool => new AiToolDefinition(tool.Name, tool.Description, tool.ParametersSchema))];
    }

    public async Task<AssistantToolOutcome> ExecuteAsync(AssistantToolContext context, AiToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(toolCall);
        var started = timeProvider.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var rawArguments = string.IsNullOrWhiteSpace(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson;
        registered.TryGetValue(toolCall.Name ?? string.Empty, out var tool);
        var toolName = tool?.Name ?? "unknown";
        var fields = new List<string>();
        var write = tool is not null && IsWriteTool(tool.Name);

        RunOutcome run;
        if (tool is null || !context.AllowedTools.Contains(tool.Name))
        {
            run = RunOutcome.Of(AssistantToolResult.Failed(AssistantToolErrorCodes.ToolNotAllowed, "This tool is not available. Answer without it or hand over to a team member."));
        }
        else if (write && context.ConversationId is null && !context.IsSellerPreview)
        {
            run = RunOutcome.Of(AssistantToolResult.Failed(AssistantToolErrorCodes.ConversationRequired, "This action is only possible inside a customer conversation."));
        }
        else if (write && !context.IsAutomationActive && tool.Name != AssistantPolicy.AlwaysAllowedTool)
        {
            run = RunOutcome.Of(AssistantToolResult.Failed(AssistantToolErrorCodes.AutomationPaused, "A team member has taken over this conversation. Do not act or reply."));
        }
        else
        {
            run = await RunAsync(tool, context, toolCall, rawArguments, fields, write, cancellationToken);
        }

        var result = run.Result;
        var dryRun = write && context.IsSellerPreview && tool!.Name != "QuoteCart";
        var trace = new AssistantToolTrace(RegistryVersion, toolName, tool?.Version ?? 0, toolCall.Id ?? string.Empty, context.ConversationId, context.IsSellerPreview,
            started, stopwatch.ElapsedMilliseconds, result.IsSuccess ? "ok" : result.ErrorCode!, fields, Hash(rawArguments), result.ResultCount, run.ReplayJson is not null, dryRun);
        var fieldList = string.Join(',', fields);
        LogTrace(logger, trace.Tool, trace.ToolVersion, trace.Outcome, trace.DurationMs, fieldList, trace.ArgumentsHash, trace.ResultCount, trace.SellerPreview, trace.Replayed);
        return new AssistantToolOutcome(run.ReplayJson ?? run.EnvelopeJson ?? Envelope(toolName, tool?.Version ?? 0, result), trace);
    }

    private async Task<RunOutcome> RunAsync(IAssistantTool tool, AssistantToolContext context, AiToolCall toolCall, string rawArguments, List<string> fields, bool write, CancellationToken cancellationToken)
    {
        using var schema = JsonDocument.Parse(tool.ParametersSchema);
        JsonDocument arguments;
        try
        {
            arguments = JsonDocument.Parse(rawArguments);
        }
        catch (JsonException)
        {
            return RunOutcome.Of(AssistantToolResult.Failed(AssistantToolErrorCodes.InvalidArguments, "The arguments are not valid JSON."));
        }

        using (arguments)
        {
            if (arguments.RootElement.ValueKind == JsonValueKind.Object)
            {
                // Field names only, and only names the schema knows; anything else is recorded generically.
                var known = schema.RootElement.TryGetProperty("properties", out var p) ? p : default;
                fields.AddRange(arguments.RootElement.EnumerateObject().Select(f => known.ValueKind == JsonValueKind.Object && known.TryGetProperty(f.Name, out _) ? f.Name : "(unknown)"));
            }

            var errors = ToolSchemaValidator.Validate(schema.RootElement, arguments.RootElement);
            if (errors.Count > 0)
            {
                return RunOutcome.Of(AssistantToolResult.Failed(AssistantToolErrorCodes.InvalidArguments, "Fix the arguments and try again.", new { errors }));
            }

            var timeout = TimeSpan.FromSeconds(aiOptions.CurrentValue.Tools.TimeoutSeconds);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            // A fresh scope per call: a timed-out tool can never share a DbContext with the next call.
            var scope = scopeFactory.CreateAsyncScope();
            var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BeginScope(new TenantContext(context.TenantId, null, null, null));
            var argumentsCopy = arguments.RootElement.Clone();
            var idempotent = write && !context.IsSellerPreview && context.ConversationId is not null;
            var callContext = scope.ServiceProvider.GetService<AssistantCallContext>();
            if (callContext is not null)
            {
                callContext.ActionId = IdGenerator.NewId();
                callContext.IdempotencyKey = idempotent ? IdempotencyKey(context, toolCall, tool.Name, argumentsCopy) : string.Empty;
            }

            var store = idempotent ? scope.ServiceProvider.GetService<IAssistantActionStore>() : null;
            var execution = Task.Run(async () =>
            {
                if (store is not null && callContext is not null)
                {
                    var replay = await store.FindCompletedAsync(callContext.IdempotencyKey, deadline.Token);
                    if (replay is not null) return RunOutcome.Replay(replay);
                }

                var result = await tool.ExecuteAsync(scope.ServiceProvider, context, argumentsCopy, deadline.Token);
                if (store is null || callContext is null || !result.IsSuccess) return RunOutcome.Of(result);

                var envelope = Envelope(tool.Name, tool.Version, result);
                await store.SaveCompletedAsync(context, tool.Name, callContext, Fingerprint(argumentsCopy), envelope, deadline.Token);
                return new RunOutcome(result, null, envelope);
            }, CancellationToken.None);
            try
            {
                return await execution.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return RunOutcome.Of(AssistantToolResult.Failed(AssistantToolErrorCodes.Timeout, "The shop's data did not answer in time. Try once more or hand over to a team member."));
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return RunOutcome.Of(AssistantToolResult.Failed(AssistantToolErrorCodes.Timeout, "The shop's data did not answer in time. Try once more or hand over to a team member."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailure(logger, ex, tool.Name);
                return RunOutcome.Of(AssistantToolResult.Failed(AssistantToolErrorCodes.Unavailable, "The shop's data is temporarily unavailable. Hand over to a team member if needed."));
            }
            finally
            {
                // Dispose only after the tool has really finished, even when we stopped waiting for it.
                _ = execution.ContinueWith(async _ =>
                {
                    tenantScope.Dispose();
                    await scope.DisposeAsync();
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
    }

    private string Envelope(string tool, int version, AssistantToolResult result)
    {
        var asOf = timeProvider.UtcNow;
        object envelope = result.IsSuccess
            ? new { ok = true, tool, version, asOf, data = result.Data }
            : new { ok = false, tool, version, asOf, error = new { code = result.ErrorCode, message = result.ErrorMessage }, data = result.Data };
        return JsonSerializer.Serialize(envelope, ResultJson);
    }

    /// <summary>Same conversation + turn (or call ID) + tool + canonical arguments → same key, across processes.</summary>
    public static string IdempotencyKey(AssistantToolContext context, AiToolCall toolCall, string tool, JsonElement arguments)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(toolCall);
        var material = $"{context.TenantId}|{context.ConversationId}|{context.TurnId ?? toolCall.Id}|{tool}|{Canonical(arguments)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    public static string Fingerprint(JsonElement arguments) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(arguments))))[..32];

    /// <summary>Property order never changes the key.</summary>
    public static string Canonical(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(',', element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(',', element.EnumerateArray().Select(Canonical)) + "]",
        _ => element.GetRawText()
    };

    // Keyed per process: the hash correlates identical calls within a run but can't be reversed (e.g. 4 phone digits).
    private static readonly byte[] HashKey = RandomNumberGenerator.GetBytes(32);

    private static string Hash(string value) => Convert.ToHexStringLower(HMACSHA256.HashData(HashKey, Encoding.UTF8.GetBytes(value)))[..16];

    private sealed record RunOutcome(AssistantToolResult Result, string? ReplayJson, string? EnvelopeJson)
    {
        public static RunOutcome Of(AssistantToolResult result) => new(result, null, null);

        public static RunOutcome Replay(string json) => new(AssistantToolResult.Success(new { }, 0), json, null);
    }
}

/// <summary>Completed write calls by idempotency key (replays), persisted in <c>assistant_actions</c>.</summary>
public interface IAssistantActionStore
{
    Task<string?> FindCompletedAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task SaveCompletedAsync(AssistantToolContext context, string tool, AssistantCallContext callContext, string argumentsFingerprint, string resultJson, CancellationToken cancellationToken);
}
