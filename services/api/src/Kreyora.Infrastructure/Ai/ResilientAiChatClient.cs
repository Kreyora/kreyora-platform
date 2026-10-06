using System.Diagnostics;
using Kreyora.Application.Ai;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Ai;

/// <summary>
/// The <see cref="IAiChatClient"/> callers get (ADR-018). In order:
/// 1. kill switch (<c>Ai:Enabled</c>) → <c>Disabled</c>;
/// 2. data policy: personal data only to providers marked NoTraining, and only if allowed → else <c>PolicyViolation</c>;
/// 3. <c>Ai:Mode</c> Fake → the deterministic fake, no network;
/// 4. Live → the requested profile; on Timeout / RateLimited / ProviderUnavailable, once to the Fallback profile,
///    within one overall deadline. InvalidRequest, InvalidResponse, ContentRefused and configuration problems
///    never fall back (another model would not fix them, or the answer is a policy outcome).
/// Options are read per call, so the kill switch takes effect on configuration reload without a restart.
/// </summary>
public sealed class ResilientAiChatClient(
    IOptionsMonitor<AiOptions> options,
    OpenAiCompatibleChatClient transport,
    FakeAiChatClient fake) : IAiChatClient
{
    private static readonly AiFailureKind[] FallbackEligible =
        [AiFailureKind.Timeout, AiFailureKind.RateLimited, AiFailureKind.ProviderUnavailable];

    public async Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return AiChatResult.Failed(AiFailureKind.Disabled, "AI is disabled.");
        }

        if (request.Messages.Count == 0)
        {
            return AiChatResult.Failed(AiFailureKind.InvalidRequest, "An AI request needs at least one message.");
        }

        if (current.Mode == AiMode.Fake)
        {
            var faked = await fake.CompleteAsync(request, cancellationToken);
            return faked with { Attempts = [request.Profile] };
        }

        var deadline = request.Timeout ?? TimeSpan.FromSeconds(current.Limits.TimeoutSeconds);
        var stopwatch = Stopwatch.StartNew();
        var attempts = new List<AiModelProfile>();

        var first = await AttemptAsync(current, request.Profile, request, deadline, attempts, cancellationToken);
        if (first.IsSuccess
            || request.Profile == AiModelProfile.Fallback
            || !FallbackEligible.Contains(first.Failure!.Value)
            || !current.Profiles.ContainsKey(nameof(AiModelProfile.Fallback)))
        {
            return first with { Attempts = attempts };
        }

        var remaining = deadline - stopwatch.Elapsed;
        if (remaining <= TimeSpan.FromMilliseconds(250))
        {
            return first with { Attempts = attempts };
        }

        var second = await AttemptAsync(current, AiModelProfile.Fallback, request, remaining, attempts, cancellationToken);
        return second with { Attempts = attempts };
    }

    private async Task<AiChatResult> AttemptAsync(
        AiOptions current,
        AiModelProfile profile,
        AiChatRequest request,
        TimeSpan timeout,
        List<AiModelProfile> attempts,
        CancellationToken cancellationToken)
    {
        attempts.Add(profile);
        if (!current.Profiles.TryGetValue(profile.ToString(), out var profileOptions)
            || !current.Providers.TryGetValue(profileOptions.Provider, out var provider)
            || string.IsNullOrWhiteSpace(provider.BaseUrl)
            || string.IsNullOrWhiteSpace(provider.ApiKey)
            || string.IsNullOrWhiteSpace(profileOptions.Model))
        {
            return AiChatResult.Failed(AiFailureKind.NotConfigured, $"AI profile {profile} is not configured.");
        }

        if (request.ContainsPersonalData && !(current.DataPolicy.AllowPersonalData && provider.NoTraining))
        {
            return AiChatResult.Failed(AiFailureKind.PolicyViolation,
                "Personal data may only be sent to an approved no-training provider (ADR-018).",
                profileOptions.Provider, profileOptions.Model);
        }

        return await transport.CompleteAsync(
            profileOptions.Provider, provider, profileOptions.Model, request, current.Limits.MaxOutputTokens, timeout, cancellationToken,
            profileOptions.ReasoningEffort);
    }
}
