using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Ai;

public enum AiMode
{
    /// <summary>Deterministic in-process fake; no network. The default.</summary>
    Fake,
    /// <summary>Calls the configured OpenAI-compatible providers.</summary>
    Live
}

/// <summary>
/// AI configuration (ADR-018). Providers and profiles are data, so switching to another provider or a paid model
/// is a configuration change only. Disabled by default.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Global kill switch. When false every request fails fast with <c>Disabled</c>.</summary>
    public bool Enabled { get; set; }

    public AiMode Mode { get; set; } = AiMode.Fake;

    /// <summary>Keyed by provider name, e.g. <c>OpenRouter</c>, <c>GoogleAiStudio</c>, <c>OpenAi</c>.</summary>
    public Dictionary<string, AiProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keyed by profile name: <c>Primary</c> (required in Live mode) and optional <c>Fallback</c>.</summary>
    public Dictionary<string, AiProfileOptions> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AiLimitsOptions Limits { get; set; } = new();

    public AiDataPolicyOptions DataPolicy { get; set; } = new();

    /// <summary>Embedding model used to index approved knowledge (M09-S03, ADR-019).</summary>
    public AiEmbeddingOptions Embeddings { get; set; } = new();

    /// <summary>Knowledge retrieval thresholds and budgets (ADR-019; tuned in S08).</summary>
    public AiRetrievalOptions Retrieval { get; set; } = new();

    /// <summary>Read-tool limits (M09-S04).</summary>
    public AiToolOptions Tools { get; set; } = new();
}

public sealed class AiToolOptions
{
    /// <summary>Deadline for one tool execution (Q9); the turn budget is S06.</summary>
    public int TimeoutSeconds { get; set; } = 3;

    /// <summary>Available quantity at or below which stock shows as low (Q3); exact counts are never shown.</summary>
    public int LowStockThreshold { get; set; } = 3;
}

public sealed class AiEmbeddingOptions
{
    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>Requested vector size (the provider's <c>dimensions</c> parameter).</summary>
    public int Dimensions { get; set; } = 768;

    public int BatchSize { get; set; } = 32;
}

public sealed class AiRetrievalOptions
{
    /// <summary>Combined score at or above which a passage is relevant.</summary>
    public double RelevantThreshold { get; set; } = 0.62;

    /// <summary>Combined score at or above which a passage is low-confidence (below: not returned).</summary>
    public double LowConfidenceThreshold { get; set; } = 0.55;

    /// <summary>Weight of the lexical-overlap boost added to cosine similarity.</summary>
    public double LexicalWeight { get; set; } = 0.1;

    /// <summary>Lexical-only fallback thresholds (share of query words found in the passage).</summary>
    public double LexicalRelevantThreshold { get; set; } = 0.5;

    public double LexicalLowConfidenceThreshold { get; set; } = 0.25;

    public int TopK { get; set; } = 4;

    public int MaxCharacters { get; set; } = 3000;
}

public sealed class AiProviderOptions
{
    /// <summary>OpenAI-compatible base URL; <c>/chat/completions</c> is appended.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Secret. Supplied by user secrets or environment only; never logged.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// True only for endpoints whose terms forbid training on and retaining inputs (paid, approved). Free tiers are
    /// false. Personal data may only ever be sent to providers marked true.
    /// </summary>
    public bool NoTraining { get; set; }

    /// <summary>Name of the output-limit field; some providers (e.g. newer OpenAI models) use <c>max_completion_tokens</c>.</summary>
    public string MaxTokensParameter { get; set; } = "max_tokens";

    /// <summary>Name of the reasoning control field; OpenRouter and Gemini's compatibility endpoint both accept <c>reasoning_effort</c>.</summary>
    public string ReasoningEffortParameter { get; set; } = "reasoning_effort";

    /// <summary>Optional non-secret headers (e.g. OpenRouter attribution headers).</summary>
    public Dictionary<string, string> ExtraHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AiProfileOptions
{
    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Optional reasoning ("thinking") level sent to the model, e.g. <c>none</c> or <c>low</c>. Thinking models otherwise
    /// spend the output budget on hidden reasoning, which slows replies and can cut them off (observed in M09-S01).
    /// Null: not sent (models that reject the field, e.g. Gemma on Google AI Studio).
    /// </summary>
    public string? ReasoningEffort { get; set; }
}

public sealed class AiLimitsOptions
{
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Also the platform cap for a shop's assistant output budget (M09-S02).</summary>
    public int MaxOutputTokens { get; set; } = 800;

    /// <summary>Platform cap for a shop's tool steps per reply.</summary>
    public int MaxToolSteps { get; set; } = 6;

    /// <summary>Platform cap for a shop's assistant replies per conversation per hour.</summary>
    public int MaxRepliesPerConversationPerHour { get; set; } = 60;
}

public sealed class AiDataPolicyOptions
{
    /// <summary>
    /// Whether real customer content may be sent at all. Requires every provider used by a profile to be
    /// <see cref="AiProviderOptions.NoTraining"/>, and owner approval of that provider's terms (ADR-018).
    /// </summary>
    public bool AllowPersonalData { get; set; }
}

/// <summary>Start-up validation for <see cref="AiOptions"/>; the API refuses to start on an unsafe configuration.</summary>
public sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public static readonly string[] ProfileNames = ["Primary", "Fallback"];

    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        var errors = Errors(options).ToList();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static IEnumerable<string> Errors(AiOptions options)
    {
        if (options.Limits.TimeoutSeconds is < 1 or > 120)
        {
            yield return "Ai:Limits:TimeoutSeconds must be between 1 and 120.";
        }

        if (options.Limits.MaxOutputTokens is < 50 or > 8000)
        {
            yield return "Ai:Limits:MaxOutputTokens must be between 50 and 8000.";
        }

        if (options.Limits.MaxToolSteps is < 1 or > 12)
        {
            yield return "Ai:Limits:MaxToolSteps must be between 1 and 12.";
        }

        if (options.Limits.MaxRepliesPerConversationPerHour is < 1 or > 600)
        {
            yield return "Ai:Limits:MaxRepliesPerConversationPerHour must be between 1 and 600.";
        }

        foreach (var profileName in options.Profiles.Keys)
        {
            if (!ProfileNames.Contains(profileName, StringComparer.OrdinalIgnoreCase))
            {
                yield return $"Ai:Profiles:{profileName} is not a known profile (use Primary or Fallback).";
            }
        }

        foreach (var (providerName, provider) in options.Providers)
        {
            if (!string.IsNullOrWhiteSpace(provider.BaseUrl)
                && (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            {
                yield return $"Ai:Providers:{providerName}:BaseUrl must be an absolute HTTPS URL.";
            }
        }

        if (options.Embeddings.Dimensions is < 64 or > 4096)
        {
            yield return "Ai:Embeddings:Dimensions must be between 64 and 4096.";
        }

        if (options.Embeddings.BatchSize is < 1 or > 256)
        {
            yield return "Ai:Embeddings:BatchSize must be between 1 and 256.";
        }

        var retrieval = options.Retrieval;
        if (retrieval.LowConfidenceThreshold < 0 || retrieval.RelevantThreshold > 1 || retrieval.LowConfidenceThreshold > retrieval.RelevantThreshold
            || retrieval.LexicalLowConfidenceThreshold < 0 || retrieval.LexicalRelevantThreshold > 1 || retrieval.LexicalLowConfidenceThreshold > retrieval.LexicalRelevantThreshold)
        {
            yield return "Ai:Retrieval thresholds must be between 0 and 1, with the low-confidence threshold not above the relevant threshold.";
        }

        if (retrieval.LexicalWeight is < 0 or > 1) yield return "Ai:Retrieval:LexicalWeight must be between 0 and 1.";
        if (retrieval.TopK is < 1 or > 20) yield return "Ai:Retrieval:TopK must be between 1 and 20.";
        if (retrieval.MaxCharacters is < 200 or > 20000) yield return "Ai:Retrieval:MaxCharacters must be between 200 and 20000.";
        if (options.Tools.TimeoutSeconds is < 1 or > 30) yield return "Ai:Tools:TimeoutSeconds must be between 1 and 30.";
        if (options.Tools.LowStockThreshold is < 0 or > 100) yield return "Ai:Tools:LowStockThreshold must be between 0 and 100.";

        if (options.Mode == AiMode.Live && !string.IsNullOrWhiteSpace(options.Embeddings.Provider))
        {
            if (!options.Providers.TryGetValue(options.Embeddings.Provider, out var embeddingProvider))
            {
                yield return $"Ai:Embeddings:Provider '{options.Embeddings.Provider}' is not configured under Ai:Providers.";
            }
            else if (string.IsNullOrWhiteSpace(embeddingProvider.ApiKey) || string.IsNullOrWhiteSpace(embeddingProvider.BaseUrl))
            {
                yield return $"Ai:Providers:{options.Embeddings.Provider} needs BaseUrl and ApiKey for embeddings in Live mode.";
            }

            if (string.IsNullOrWhiteSpace(options.Embeddings.Model))
            {
                yield return "Ai:Embeddings:Model is required when an embedding provider is configured.";
            }
        }

        if (options.Mode == AiMode.Live)
        {
            if (!options.Profiles.ContainsKey("Primary"))
            {
                yield return "Ai:Profiles:Primary is required in Live mode.";
            }

            foreach (var (profileName, profile) in options.Profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Model))
                {
                    yield return $"Ai:Profiles:{profileName}:Model is required in Live mode.";
                }

                if (!options.Providers.TryGetValue(profile.Provider, out var provider))
                {
                    yield return $"Ai:Profiles:{profileName}:Provider '{profile.Provider}' is not configured under Ai:Providers.";
                    continue;
                }

                if (string.IsNullOrWhiteSpace(provider.BaseUrl))
                {
                    yield return $"Ai:Providers:{profile.Provider}:BaseUrl is required in Live mode.";
                }

                if (string.IsNullOrWhiteSpace(provider.ApiKey))
                {
                    yield return $"Ai:Providers:{profile.Provider}:ApiKey is required in Live mode (set it via user secrets or environment).";
                }
            }
        }

        if (options.DataPolicy.AllowPersonalData)
        {
            foreach (var (profileName, profile) in options.Profiles)
            {
                if (!options.Providers.TryGetValue(profile.Provider, out var provider) || !provider.NoTraining)
                {
                    yield return $"Ai:DataPolicy:AllowPersonalData requires a NoTraining provider, but profile {profileName} uses '{profile.Provider}'. Free tiers may never receive personal data (ADR-018).";
                }
            }
        }
    }
}
