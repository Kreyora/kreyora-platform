using System.Globalization;
using System.Text.Json;
using Kreyora.Application.Assistant;
using Kreyora.Application.Audit;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Assistant;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant;

/// <summary>Workspace assistant policy (M09-S02): safe defaults on first read, validated updates within platform caps, audited.</summary>
public sealed class AssistantPolicyService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IAuditEventService auditEvents,
    IOptionsMonitor<AiOptions> aiOptions,
    ITimeProvider timeProvider) : IAssistantPolicyService, IAssistantPolicyQuery
{
    public async Task<Result<AssistantPolicyItem>> GetAsync(CancellationToken cancellationToken = default) =>
        Result<AssistantPolicyItem>.Success(ToItem(await GetOrCreateAsync(cancellationToken), Caps(), clamp: false));

    public async Task<AssistantPolicyItem> GetEffectiveAsync(CancellationToken cancellationToken = default) =>
        ToItem(await GetOrCreateAsync(cancellationToken), Caps(), clamp: true);

    public async Task<Result<AssistantPolicyItem>> UpdateAsync(UpdateAssistantPolicyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = tenantContext.RequireCurrent();
        var policy = await GetOrCreateAsync(cancellationToken);
        var entry = dbContext.Entry(policy);
        var currentVersion = entry.Property<uint>("xmin").CurrentValue.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(currentVersion, request.Version, StringComparison.Ordinal))
        {
            return AssistantProblemCodes.Problem<AssistantPolicyItem>(AssistantProblemCodes.PolicyChanged,
                "The assistant settings were changed by someone else. Refresh to see the latest.", 409);
        }

        var settings = new AssistantPolicySettings(request.Enabled, request.ReplyStyle, request.SupportedLanguages ?? [], request.Tone,
            request.BrandNote, request.BusinessHours ?? [], request.OutsideHoursBehavior, request.UnrecognizedMediaBehavior,
            request.EscalationKeywords ?? [], request.AllowedTools ?? [], request.MaxToolSteps, request.MaxRepliesPerConversationPerHour,
            request.MaxOutputTokens);
        var errors = AssistantPolicyRules.Validate(settings, Caps());
        if (errors.Count > 0)
        {
            return AssistantProblemCodes.Problem<AssistantPolicyItem>(AssistantProblemCodes.PolicyInvalid,
                "Some assistant settings are not valid.", 400, errors);
        }

        var before = Snapshot(policy);
        policy.Apply(settings, context.UserId ?? "system", timeProvider.UtcNow);
        var changed = Snapshot(policy).Where(kv => !string.Equals(before[kv.Key], kv.Value, StringComparison.Ordinal)).Select(kv => kv.Key).ToList();

        try
        {
            // Audit metadata lists changed field names only, never values (brand note, keywords).
            await auditEvents.AppendAsync(new AuditEventWrite("assistant.policy.updated", "assistant_policy", policy.Id,
                Metadata: JsonSerializer.Serialize(new { changed })), cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return AssistantProblemCodes.Problem<AssistantPolicyItem>(AssistantProblemCodes.PolicyChanged,
                "The assistant settings were changed by someone else. Refresh to see the latest.", 409);
        }

        return Result<AssistantPolicyItem>.Success(ToItem(policy, Caps(), clamp: false));
    }

    private async Task<AssistantPolicy> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var context = tenantContext.RequireCurrent();
        var existing = await dbContext.AssistantPolicies.SingleOrDefaultAsync(p => p.TenantId == context.TenantId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = AssistantPolicy.CreateDefault(context.TenantId);
        dbContext.AssistantPolicies.Add(created);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException)
        {
            // Another request created the defaults first (unique tenant index): use that row.
            dbContext.Entry(created).State = EntityState.Detached;
            return await dbContext.AssistantPolicies.SingleAsync(p => p.TenantId == context.TenantId, cancellationToken);
        }
    }

    private AssistantPlatformCaps Caps()
    {
        var limits = aiOptions.CurrentValue.Limits;
        return new AssistantPlatformCaps(limits.MaxToolSteps, limits.MaxRepliesPerConversationPerHour, limits.MaxOutputTokens);
    }

    private AssistantPolicyItem ToItem(AssistantPolicy policy, AssistantPlatformCaps caps, bool clamp) => new(
        policy.Enabled,
        policy.ReplyStyle,
        policy.SupportedLanguages,
        policy.Tone,
        policy.BrandNote,
        policy.BusinessHours,
        AssistantPolicy.TimeZoneId,
        policy.OutsideHoursBehavior,
        policy.UnrecognizedMediaBehavior,
        policy.EscalationKeywords,
        policy.AllowedTools,
        clamp ? Math.Min(policy.MaxToolSteps, caps.MaxToolSteps) : policy.MaxToolSteps,
        clamp ? Math.Min(policy.MaxRepliesPerConversationPerHour, caps.MaxRepliesPerConversationPerHour) : policy.MaxRepliesPerConversationPerHour,
        clamp ? Math.Min(policy.MaxOutputTokens, caps.MaxOutputTokens) : policy.MaxOutputTokens,
        policy.ReviewedAt,
        dbContext.Entry(policy).Property<uint>("xmin").CurrentValue.ToString(CultureInfo.InvariantCulture),
        AssistantPolicy.FixedEscalationCategories,
        [.. AssistantPolicy.ReadTools, AssistantPolicy.AlwaysAllowedTool],
        caps);

    private static Dictionary<string, string> Snapshot(AssistantPolicy p) => new(StringComparer.Ordinal)
    {
        ["enabled"] = p.Enabled.ToString(),
        ["replyStyle"] = p.ReplyStyle.ToString(),
        ["supportedLanguages"] = string.Join(',', p.SupportedLanguages),
        ["tone"] = p.Tone.ToString(),
        ["brandNote"] = p.BrandNote ?? string.Empty,
        ["businessHours"] = JsonSerializer.Serialize(p.BusinessHours),
        ["outsideHoursBehavior"] = p.OutsideHoursBehavior.ToString(),
        ["unrecognizedMediaBehavior"] = p.UnrecognizedMediaBehavior.ToString(),
        ["escalationKeywords"] = string.Join('\u001f', p.EscalationKeywords),
        ["allowedTools"] = string.Join(',', p.AllowedTools),
        ["maxToolSteps"] = p.MaxToolSteps.ToString(CultureInfo.InvariantCulture),
        ["maxRepliesPerConversationPerHour"] = p.MaxRepliesPerConversationPerHour.ToString(CultureInfo.InvariantCulture),
        ["maxOutputTokens"] = p.MaxOutputTokens.ToString(CultureInfo.InvariantCulture)
    };
}
