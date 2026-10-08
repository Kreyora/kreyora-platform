using Hangfire;
using Kreyora.Application.Conversations;
using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Customers;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Conversations;

// M09-S08 Q8: customer names for the inbox. Staff-only; never part of an assistant prompt; cleared by erasure.

/// <summary>Schedules a lookup for customers whose profile is due, after the webhook transaction commits.</summary>
public sealed partial class CustomerProfileHook(
    IOptionsMonitor<InstagramMessagingOptions> options,
    ITimeProvider timeProvider,
    ILogger<CustomerProfileHook> logger,
    IBackgroundJobClient? jobs = null) : ICustomerProfileHook
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);
    private readonly Dictionary<string, string> due = new(StringComparer.Ordinal); // identity → tenant

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not schedule a customer profile lookup; it is retried on the next message")]
    private static partial void LogScheduleFailed(ILogger logger, Exception ex);

    public void IdentitySeen(string tenantId, string identityId, DateTimeOffset? profileCheckedAt)
    {
        var o = options.CurrentValue;
        if (o.ProfileLookupEnabled && CustomerChannelIdentity.ProfileCheckDue(profileCheckedAt, timeProvider.UtcNow, TimeSpan.FromDays(o.ProfileRefreshDays)))
        {
            due[identityId] = tenantId;
        }
    }

    public void Flush()
    {
        foreach (var (identityId, tenantId) in due)
        {
            try
            {
                jobs?.Schedule<CustomerProfileLookupJob>(job => job.RunAsync(tenantId, identityId), Delay);
            }
            catch (Exception ex)
            {
                LogScheduleFailed(logger, ex);
            }
        }

        Discard();
    }

    public void Discard() => due.Clear();
}

/// <summary>Runs one lookup inside the identity's tenant (from the job, re-resolved by the job runner).</summary>
public sealed class CustomerProfileLookupJob(IServiceScopeFactory scopeFactory)
{
    public async Task<string> RunAsync(string tenantId, string identityId)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var outcome = "tenant_unavailable";
        try
        {
            await services.GetRequiredService<ITenantJobRunner>().RunAsync(new TenantJobEnvelope(tenantId, "customer-profile-lookup", "{}"), async cancellationToken =>
            {
                outcome = await services.GetRequiredService<ICustomerProfileLookupService>().LookupAsync(identityId, cancellationToken);
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("inactive or unavailable", StringComparison.Ordinal))
        {
            return outcome;
        }

        return outcome;
    }
}

public sealed partial class CustomerProfileLookupService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IInstagramGraphClient graph,
    ISecretEncryptionService encryption,
    IOptionsMonitor<InstagramMessagingOptions> options,
    ITimeProvider timeProvider,
    ILogger<CustomerProfileLookupService> logger) : ICustomerProfileLookupService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Customer profile lookup for identity {IdentityId}: {Outcome}")]
    private static partial void LogOutcome(ILogger logger, string identityId, string outcome);

    public async Task<string> LookupAsync(string identityId, CancellationToken cancellationToken = default)
    {
        var outcome = await RunAsync(identityId, cancellationToken);
        LogOutcome(logger, identityId, outcome);
        return outcome;
    }

    private async Task<string> RunAsync(string identityId, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var o = options.CurrentValue;
        if (!o.ProfileLookupEnabled) return "disabled";

        var identity = await dbContext.CustomerChannelIdentities.SingleOrDefaultAsync(i => i.Id == identityId && i.TenantId == tenantId, cancellationToken);
        if (identity is null) return "not_found";
        if (identity.ErasedAt.HasValue) return "erased";
        var now = timeProvider.UtcNow;
        if (!CustomerChannelIdentity.ProfileCheckDue(identity.ProfileCheckedAt, now, TimeSpan.FromDays(o.ProfileRefreshDays))) return "not_due";
        if (identity.Channel != ChannelType.Instagram) return "unsupported_channel";

        var connection = await dbContext.ChannelConnections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == identity.ConnectionId && c.TenantId == tenantId, cancellationToken);
        if (connection?.EncryptedCredentials is null || connection.Status != ChannelConnectionStatus.Active) return "no_credentials";

        var profile = await graph.GetUserProfileAsync(encryption.Decrypt(connection.EncryptedCredentials), identity.ExternalUserId, cancellationToken);
        if (!profile.IsSuccess && profile.Kind is InstagramValidationKind.Transient or InstagramValidationKind.Throttled)
            return "provider_transient"; // not stamped: the next message tries again

        identity.ApplyProfile(profile.Name, profile.Username, now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return "conflict"; // the identity changed meanwhile (e.g. erased); the next message tries again
        }

        return profile.IsSuccess ? "updated" : $"provider_{profile.Kind.ToString().ToLowerInvariant()}";
    }
}
