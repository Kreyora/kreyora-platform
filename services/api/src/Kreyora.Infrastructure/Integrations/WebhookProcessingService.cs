using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.Integrations;

public sealed partial class WebhookProcessingService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    IAuditEventService auditEvents,
    IChannelProviderRegistry providerRegistry,
    ILogger<WebhookProcessingService> logger,
    IServiceProvider? serviceProvider = null) : IWebhookProcessingService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully normalized and processed webhook event {EventId} with {Count} inbound events for tenant {TenantId}")]
    private static partial void LogProcessingSuccess(ILogger logger, string eventId, int count, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to update outbound message status receipt for MessageId {MessageId}")]
    private static partial void LogStatusReceiptHookFailed(ILogger logger, Exception ex, string messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped duplicate inbound message {MessageId} for connection {ConnectionId} in webhook event {EventId}")]
    private static partial void LogDuplicateMessageSkipped(ILogger logger, string messageId, string connectionId, string eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process webhook event {EventId} for tenant {TenantId}. New status: {Status}, Classification: {Classification}")]
    private static partial void LogProcessingFailed(ILogger logger, Exception ex, string eventId, string status, string classification, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Replayed webhook event {EventId} with idempotency key {IdempotencyKey} for tenant {TenantId}")]
    private static partial void LogWebhookReplayed(ILogger logger, string eventId, string idempotencyKey, string tenantId);

    public async Task<WebhookProcessingResult> ProcessWebhookEventAsync(
        string webhookEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookEventId);

        var ev = await dbContext.WebhookEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == webhookEventId, cancellationToken);

        if (ev is null)
        {
            return WebhookProcessingResult.Failed(
                webhookEventId,
                WebhookProcessingStatus.Failed,
                $"Webhook event '{webhookEventId}' not found.",
                WebhookFailureClassification.Permanent);
        }

        if (ev.ProcessingStatus == WebhookProcessingStatus.Processed)
        {
            return WebhookProcessingResult.Duplicate(webhookEventId);
        }

        if (ev.ProcessingStatus == WebhookProcessingStatus.DeadLetter)
        {
            return WebhookProcessingResult.Failed(
                webhookEventId,
                WebhookProcessingStatus.DeadLetter,
                "Webhook event is in DeadLetter queue. An authorized replay is required.",
                WebhookFailureClassification.Permanent);
        }

        using var scope = tenantContext.BeginScope(new TenantContext(ev.TenantId, null, null, null));
        ev.MarkProcessing();
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            if (ev.IsPurged)
            {
                throw new InvalidOperationException("Raw webhook payload has already been purged per ADR-012 policy.");
            }

            var connection = await dbContext.ChannelConnections
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.TenantId == ev.TenantId && c.Id == ev.ConnectionId, cancellationToken);

            if (connection is null)
            {
                throw new InvalidOperationException($"Connection '{ev.ConnectionId}' not found for tenant '{ev.TenantId}'.");
            }

            if (connection.Status == ChannelConnectionStatus.Disabled || connection.Status == ChannelConnectionStatus.Revoked)
            {
                throw new InvalidOperationException($"Connection '{ev.ConnectionId}' is in '{connection.Status}' status and cannot process webhooks.");
            }

            var provider = providerRegistry.GetProvider(ev.Channel);

            var headers = ParseHeaders(ev.Headers);
            var rawPayload = new RawWebhookPayload(
                ev.RawPayload,
                headers,
                "application/json",
                ev.TenantId,
                ev.ConnectionId,
                ev.Id,
                ev.Channel,
                ev.ReceivedAt);

            var envelopes = await provider.NormalizeInboundAsync(rawPayload, cancellationToken);

            var normalizedCount = 0;
            foreach (var envelope in envelopes)
            {
                if (envelope.SchemaVersion != NormalizedInboundEnvelope.CurrentSchemaVersion)
                {
                    throw new InvalidOperationException(
                        $"Unsupported schema version '{envelope.SchemaVersion}'. Expected '{NormalizedInboundEnvelope.CurrentSchemaVersion}'.");
                }

                var providerMessageId = ExtractProviderMessageId(envelope.Payload);
                if (!string.IsNullOrWhiteSpace(providerMessageId))
                {
                    var exists = await dbContext.InboundEvents
                        .IgnoreQueryFilters()
                        .AnyAsync(i => i.ConnectionId == ev.ConnectionId && i.ProviderMessageId == providerMessageId, cancellationToken);

                    if (exists)
                    {
                        LogDuplicateMessageSkipped(logger, providerMessageId, ev.ConnectionId, ev.Id);
                        continue;
                    }
                }

                var payloadJson = JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType());
                var eventType = GetPayloadType(envelope.Payload);

                var inbound = InboundEvent.Create(
                    ev.TenantId,
                    ev.ConnectionId,
                    ev.Id,
                    ev.Channel,
                    providerMessageId,
                    envelope.SchemaVersion,
                    eventType,
                    payloadJson,
                    envelope.OccurredAt);

                dbContext.InboundEvents.Add(inbound);
                normalizedCount++;

                if (envelope.Payload is MessageStatusUpdatedPayload statusPayload)
                {
                    try
                    {
                        var outboundService = serviceProvider?.GetService<IOutboundMessageService>();
                        if (outboundService is not null)
                        {
                            await outboundService.ProcessStatusReceiptAsync(
                                ev.ConnectionId,
                                statusPayload.MessageId,
                                statusPayload.Status,
                                statusPayload.Timestamp,
                                cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogStatusReceiptHookFailed(logger, ex, statusPayload.MessageId);
                    }
                }
            }

            ev.RecordSuccess(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);

            LogProcessingSuccess(logger, ev.Id, normalizedCount, ev.TenantId);
            return WebhookProcessingResult.Success(ev.Id, normalizedCount);
        }
        catch (Exception ex)
        {
            var classification = WebhookFailureClassifier.Classify(ex);
            var retryPolicy = new WebhookRetryPolicy(ev.MaxAttempts);
            ev.RecordFailure(ex.Message, classification, DateTimeOffset.UtcNow, retryPolicy);

            await dbContext.SaveChangesAsync(cancellationToken);

            LogProcessingFailed(logger, ex, ev.Id, ev.ProcessingStatus.ToString(), classification.ToString(), ev.TenantId);
            return WebhookProcessingResult.Failed(ev.Id, ev.ProcessingStatus, ex.Message, ev.FailureClassification ?? classification);
        }
    }

    public async Task<Result<PagedResult<WebhookDeadLetterDto>>> GetDeadLetterEventsAsync(
        WebhookDeadLetterQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        var dbQuery = dbContext.WebhookEvents
            .Where(e => e.ProcessingStatus == WebhookProcessingStatus.DeadLetter);

        if (query.Channel.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.Channel == query.Channel.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var items = await dbQuery
            .OrderByDescending(e => e.DeadLetteredAt ?? e.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new WebhookDeadLetterDto(
                e.Id,
                e.TenantId,
                e.ConnectionId,
                e.Channel,
                e.ProviderEventId,
                e.EventType,
                e.ErrorMessage,
                e.FailureClassification,
                e.AttemptCount,
                e.MaxAttempts,
                e.OccurredAt,
                e.ReceivedAt,
                e.DeadLetteredAt,
                e.CorrelationId))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<WebhookDeadLetterDto>>.Success(
            new PagedResult<WebhookDeadLetterDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
    }

    public async Task<Result<WebhookReplayResult>> ReplayWebhookEventAsync(
        string webhookEventId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookEventId);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result<WebhookReplayResult>.ValidationError("An Idempotency-Key is required to replay a webhook event.");
        }

        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        var current = tenantContext.RequireCurrent();

        var ev = await dbContext.WebhookEvents
            .FirstOrDefaultAsync(e => e.Id == webhookEventId, cancellationToken);

        if (ev is null)
        {
            return Result<WebhookReplayResult>.NotFound($"Webhook event '{webhookEventId}' was not found.");
        }

        if (ev.ProcessingStatus == WebhookProcessingStatus.Received ||
            ev.ProcessingStatus == WebhookProcessingStatus.Processing)
        {
            return Result<WebhookReplayResult>.Success(WebhookReplayResult.Success(ev.Id, idempotencyKey));
        }

        if (ev.ProcessingStatus != WebhookProcessingStatus.DeadLetter &&
            ev.ProcessingStatus != WebhookProcessingStatus.Failed)
        {
            return Result<WebhookReplayResult>.ValidationError(
                $"Cannot replay webhook event in status '{ev.ProcessingStatus}'. Only DeadLetter or Failed events can be replayed.");
        }

        ev.Replay(DateTimeOffset.UtcNow);

        await auditEvents.AppendAsync(new AuditEventWrite(
            Action: "integrations.webhook.replayed",
            TargetType: "webhook-event",
            TargetId: ev.Id,
            Metadata: JsonSerializer.Serialize(new
            {
                connectionId = ev.ConnectionId,
                channel = ev.Channel.ToString(),
                idempotencyKey = idempotencyKey
            })), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        LogWebhookReplayed(logger, ev.Id, idempotencyKey, current.TenantId);
        return Result<WebhookReplayResult>.Success(WebhookReplayResult.Success(ev.Id, idempotencyKey));
    }

    private static string? ExtractProviderMessageId(NormalizedInboundPayload payload) => payload switch
    {
        TextMessageReceivedPayload text => text.MessageId,
        MediaMessageReceivedPayload media => media.MessageId,
        MessageStatusUpdatedPayload status => status.MessageId,
        ReactionReceivedPayload reaction => reaction.MessageId,
        _ => null
    };

    private static string GetPayloadType(NormalizedInboundPayload payload) => payload switch
    {
        TextMessageReceivedPayload => "text",
        MediaMessageReceivedPayload => "media",
        MessageStatusUpdatedPayload => "status",
        CustomerProfileUpdatedPayload => "profile",
        ReactionReceivedPayload => "reaction",
        _ => payload.GetType().Name.ToLowerInvariant()
    };

    private static Dictionary<string, string> ParseHeaders(string headersJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
