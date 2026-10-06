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
