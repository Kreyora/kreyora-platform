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
/// Versioned, allowlisted read-tool registry (M09-S04). A call runs only if the tool is registered, is a read tool and
/// is allowed by the context; arguments are strictly validated; each run gets its own DI scope in the context's tenant
/// and a deadline. Every outcome is a JSON envelope for the model plus a values-free trace. Nothing throws.
/// </summary>
public sealed partial class AssistantToolRegistry(
    IEnumerable<IAssistantTool> tools,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AiOptions> aiOptions,
    ITimeProvider timeProvider,
    ILogger<AssistantToolRegistry> logger) : IAssistantToolRegistry
{
    public const string RegistryVersion = "kreyora-read-tools.v1";

    public static readonly JsonSerializerOptions ResultJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly Dictionary<string, IAssistantTool> registered = tools
        .Where(t => AssistantPolicy.ReadTools.Contains(t.Name))
        .ToDictionary(t => t.Name, StringComparer.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Assistant tool {Tool} v{ToolVersion} → {Outcome} in {DurationMs} ms (fields {ArgumentFields}, args {ArgumentsHash}, results {ResultCount}, preview {SellerPreview})")]
    private static partial void LogTrace(ILogger logger, string tool, int toolVersion, string outcome, long durationMs, string argumentFields, string argumentsHash, int resultCount, bool sellerPreview);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assistant tool {Tool} failed unexpectedly")]
    private static partial void LogFailure(ILogger logger, Exception ex, string tool);

    public string Version => RegistryVersion;

    public IReadOnlyList<AssistantToolDescriptor> Describe(IReadOnlyList<string> enabledTools) =>
        [.. AssistantPolicy.ReadTools.Where(registered.ContainsKey).Select(name => registered[name]).Select(tool =>
            new AssistantToolDescriptor(tool.Name, tool.Version, tool.Description, JsonDocument.Parse(tool.ParametersSchema).RootElement.Clone(), enabledTools.Contains(tool.Name)))];

    public IReadOnlyList<AiToolDefinition> GetDefinitions(AssistantToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [.. AssistantPolicy.ReadTools.Where(name => registered.ContainsKey(name) && context.AllowedTools.Contains(name))
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

        AssistantToolResult result;
        if (tool is null || !context.AllowedTools.Contains(tool.Name) || AssistantPolicy.WriteTools.Contains(tool.Name))
        {
            result = AssistantToolResult.Failed(AssistantToolErrorCodes.ToolNotAllowed, "This tool is not available. Answer without it or hand over to a team member.");
        }
        else
        {
            result = await RunAsync(tool, context, rawArguments, fields, cancellationToken);
        }

        var trace = new AssistantToolTrace(RegistryVersion, toolName, tool?.Version ?? 0, toolCall.Id ?? string.Empty, context.ConversationId, context.IsSellerPreview,
            started, stopwatch.ElapsedMilliseconds, result.IsSuccess ? "ok" : result.ErrorCode!, fields, Hash(rawArguments), result.ResultCount);
        var fieldList = string.Join(',', fields);
        LogTrace(logger, trace.Tool, trace.ToolVersion, trace.Outcome, trace.DurationMs, fieldList, trace.ArgumentsHash, trace.ResultCount, trace.SellerPreview);
        return new AssistantToolOutcome(Envelope(toolName, tool?.Version ?? 0, result), trace);
    }

    private async Task<AssistantToolResult> RunAsync(IAssistantTool tool, AssistantToolContext context, string rawArguments, List<string> fields, CancellationToken cancellationToken)
    {
        using var schema = JsonDocument.Parse(tool.ParametersSchema);
        JsonDocument arguments;
        try
        {
            arguments = JsonDocument.Parse(rawArguments);
        }
        catch (JsonException)
        {
            return AssistantToolResult.Failed(AssistantToolErrorCodes.InvalidArguments, "The arguments are not valid JSON.");
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
                return AssistantToolResult.Failed(AssistantToolErrorCodes.InvalidArguments, "Fix the arguments and try again.", new { errors });
            }

            var timeout = TimeSpan.FromSeconds(aiOptions.CurrentValue.Tools.TimeoutSeconds);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            // A fresh scope per call: a timed-out tool can never share a DbContext with the next call.
            var scope = scopeFactory.CreateAsyncScope();
            var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BeginScope(new TenantContext(context.TenantId, null, null, null));
            var argumentsCopy = arguments.RootElement.Clone();
            var execution = Task.Run(() => tool.ExecuteAsync(scope.ServiceProvider, context, argumentsCopy, deadline.Token), CancellationToken.None);
            try
            {
                return await execution.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return AssistantToolResult.Failed(AssistantToolErrorCodes.Timeout, "The shop's data did not answer in time. Try once more or hand over to a team member.");
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return AssistantToolResult.Failed(AssistantToolErrorCodes.Timeout, "The shop's data did not answer in time. Try once more or hand over to a team member.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailure(logger, ex, tool.Name);
                return AssistantToolResult.Failed(AssistantToolErrorCodes.Unavailable, "The shop's data is temporarily unavailable. Hand over to a team member if needed.");
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

    // Keyed per process: the hash correlates identical calls within a run but can't be reversed (e.g. 4 phone digits).
    private static readonly byte[] HashKey = RandomNumberGenerator.GetBytes(32);

    private static string Hash(string value) => Convert.ToHexStringLower(HMACSHA256.HashData(HashKey, Encoding.UTF8.GetBytes(value)))[..16];
}
