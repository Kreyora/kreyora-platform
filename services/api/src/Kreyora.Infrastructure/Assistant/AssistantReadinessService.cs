using Kreyora.Application.Assistant;
using Kreyora.Application.Models;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant;

/// <summary>
/// "Can the assistant answer for this shop?" (M09-S02 §C). Computed on every read, never stored, so it cannot drift.
/// Reuses the storefront's own readiness instead of defining "setup complete" twice.
/// </summary>
public sealed class AssistantReadinessService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IStoreReadinessQuery storeReadiness,
    IAssistantEntitlementQuery entitlements,
    IOptionsMonitor<AiOptions> aiOptions) : IAssistantReadinessService, IAssistantActivationQuery
{
    public const string StoreReady = "store_ready";
    public const string ChannelConnected = "channel_connected";
    public const string PolicyReviewed = "policy_reviewed";
    public const string KnowledgeApproved = "knowledge_approved";
    public const string PlatformEnabled = "platform_enabled";
    public const string Entitled = "ai_entitled";

    public async Task<Result<AssistantReadinessItem>> GetAsync(CancellationToken cancellationToken = default) =>
        Result<AssistantReadinessItem>.Success(await BuildAsync(cancellationToken));

    public async Task<bool> IsActiveAsync(CancellationToken cancellationToken = default) => (await BuildAsync(cancellationToken)).IsActive;

    private async Task<AssistantReadinessItem> BuildAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var checks = new List<AssistantReadinessCheck>();

        // System-safe read (M09-S06): background turns have no member role, so no permission demand here.
        var store = await storeReadiness.GetAsync(cancellationToken);
        checks.Add(store is not null
            ? new AssistantReadinessCheck(StoreReady, store.CanAcceptOrders, true,
                store.CanAcceptOrders ? "The store is active and can accept orders." : "Finish the store setup (products, delivery, payment) so orders can be accepted.",
                [.. store.Blockers.Select(b => b.Code)])
            : new AssistantReadinessCheck(StoreReady, false, true, "Create and set up the store first.", ["store_missing"]));

        var channel = await dbContext.ChannelConnections.AnyAsync(c => c.TenantId == tenantId && c.Status == ChannelConnectionStatus.Active, cancellationToken);
        checks.Add(new AssistantReadinessCheck(ChannelConnected, channel, true,
            channel ? "A social channel is connected." : "Connect a social channel (Instagram) so the assistant has conversations to answer.", []));

        var policy = await dbContext.AssistantPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);
        var reviewed = policy?.ReviewedAt is not null;
        checks.Add(new AssistantReadinessCheck(PolicyReviewed, reviewed, true,
            reviewed ? "The assistant settings have been reviewed." : "Review and save the assistant settings once.", []));

        var knowledge = await dbContext.KnowledgeDocuments.AnyAsync(d => d.TenantId == tenantId && d.DeletedAt == null && d.ActiveVersionId != null, cancellationToken);
        checks.Add(new AssistantReadinessCheck(KnowledgeApproved, knowledge, false,
            knowledge ? "Approved knowledge is available." : "Recommended: add and approve FAQ, delivery or returns information.", []));

        var entitled = entitlements.IsEntitled(tenantId);
        checks.Add(new AssistantReadinessCheck(Entitled, entitled, true,
            entitled ? "This shop is enabled for the assistant." : "The assistant hasn't been enabled for this shop yet (platform operator setting).", []));

        var platform = aiOptions.CurrentValue.Enabled;
        checks.Add(new AssistantReadinessCheck(PlatformEnabled, platform, false,
            platform ? "AI is enabled on the platform." : "AI is not yet enabled on the platform (operator setting).", []));

        var sellerEnabled = policy?.Enabled ?? true; // default on (plan Q1); activation still needs the checks below
        return new AssistantReadinessItem(IsActive(sellerEnabled, platform, checks), sellerEnabled, platform, checks);
    }

    /// <summary>Effective activation (ADR-018 §4): seller toggle AND platform switch AND every required check.</summary>
    public static bool IsActive(bool sellerEnabled, bool platformEnabled, IEnumerable<AssistantReadinessCheck> checks) =>
        sellerEnabled && platformEnabled && checks.Where(c => c.Required).All(c => c.Passed);
}
