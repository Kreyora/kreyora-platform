using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.Integrations;

public sealed partial class OutboundMessageService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    IAuditEventService auditEvents,
    IChannelProviderRegistry providerRegistry,
    IConversationGate conversationGate,
    ITimeProvider timeProvider,
    ILogger<OutboundMessageService> logger) : IOutboundMessageService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Queued outbound message {MessageId} for recipient {Recipient} via connection {ConnectionId} on tenant {TenantId}")]
    private static partial void LogMessageQueued(ILogger logger, string messageId, string recipient, string connectionId, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully sent outbound message {MessageId} (ProviderMessageId: {ProviderMessageId}) on tenant {TenantId}")]
    private static partial void LogMessageSent(ILogger logger, string messageId, string providerMessageId, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed attempt {Attempt} for outbound message {MessageId} on tenant {TenantId}. New status: {Status}")]
    private static partial void LogDeliveryFailed(ILogger logger, string messageId, int attempt, OutboundMessageStatus status, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated provider status for outbound message {MessageId} to {Status} from receipt")]
    private static partial void LogStatusReceiptUpdated(ILogger logger, string messageId, MessageDeliveryStatus status);

    public async Task<OutboundMessageResult> QueueMessageAsync(
        QueueOutboundMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        var context = tenantContext.RequireCurrent();

        var connection = await dbContext.ChannelConnections
            .FirstOrDefaultAsync(c => c.Id == request.ConnectionId, cancellationToken);

        if (connection is null)
        {
            return OutboundMessageResult.Failed($"Channel connection '{request.ConnectionId}' was not found.");
        }

        if (connection.Status != ChannelConnectionStatus.Active)
        {
            return OutboundMessageResult.Failed($"Channel connection '{request.ConnectionId}' is in '{connection.Status}' status and cannot send messages.");
        }

        // Validate connection capabilities
        switch (request.MessageType)
        {
            case OutboundMessageType.Text:
                if (!connection.Capabilities.CanSendText)
                {
                    return OutboundMessageResult.Failed("Connection does not support sending text messages.");
                }
                break;

            case OutboundMessageType.Media:
                if (!connection.Capabilities.CanSendMedia)
                {
                    return OutboundMessageResult.Failed("Connection does not support sending media messages.");
                }
                break;

            case OutboundMessageType.LinkPreview:
                if (!connection.Capabilities.CanSendLinkPreview)
                {
                    return OutboundMessageResult.Failed("Connection does not support sending link preview messages.");
                }
                break;
        }

        // Conversation gate check (placeholder for M08)
        var gateResult = await conversationGate.CheckSendPermissionAsync(
            context.TenantId,
            request.ConnectionId,
            request.ConversationId,
            cancellationToken);

        if (!gateResult.Allowed)
        {
            return OutboundMessageResult.Failed(gateResult.DenialReason ?? "Message send was blocked by conversation gate.");
        }

        // Idempotency check
        var existing = await dbContext.OutboundMessages
            .FirstOrDefaultAsync(m => m.ConnectionId == request.ConnectionId && m.IdempotencyKey == request.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            return OutboundMessageResult.Success(existing.Id, existing.Status, isDuplicate: true);
        }

        var message = OutboundMessage.Create(
            tenantId: context.TenantId,
            connectionId: request.ConnectionId,
            channel: connection.Channel,
            recipientChannelId: request.RecipientChannelId,
            idempotencyKey: request.IdempotencyKey,
            messageType: request.MessageType,
            textContent: request.TextContent,
            mediaUrl: request.MediaUrl,
            mediaContentType: request.MediaContentType,
            caption: request.Caption,
            templateCode: request.TemplateCode,
            templateParametersJson: request.TemplateParametersJson,
            metadataJson: request.MetadataJson,
            conversationId: request.ConversationId,
            queuedAt: timeProvider.UtcNow,
            maxAttempts: request.MaxAttempts ?? WebhookRetryPolicy.DefaultMaxAttempts);

        dbContext.OutboundMessages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        LogMessageQueued(logger, message.Id, message.RecipientChannelId, message.ConnectionId, context.TenantId);

        return OutboundMessageResult.Success(message.Id, message.Status);
    }

    public async Task ProcessDeliveryAsync(
        string outboundMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboundMessageId);

        var message = await dbContext.OutboundMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == outboundMessageId, cancellationToken);

        if (message is null) return;

        if (message.Status != OutboundMessageStatus.Queued && message.Status != OutboundMessageStatus.Failed)
        {
            return;
        }

        using var scope = tenantContext.BeginScope(new TenantContext(message.TenantId, null, null, null));

        var connection = await dbContext.ChannelConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == message.TenantId && c.Id == message.ConnectionId, cancellationToken);

        var now = timeProvider.UtcNow;
        message.MarkSending();
        await dbContext.SaveChangesAsync(cancellationToken);

        if (connection is null || connection.Status != ChannelConnectionStatus.Active)
        {
            var err = connection is null
                ? $"Connection '{message.ConnectionId}' was not found."
                : $"Connection '{message.ConnectionId}' is not active (status: {connection.Status}).";

            var completedAt = timeProvider.UtcNow;
            var inactiveAttempt = OutboundDeliveryAttempt.Create(
                tenantId: message.TenantId,
                outboundMessageId: message.Id,
                attemptNumber: message.AttemptCount,
                channel: message.Channel,
                connectionId: message.ConnectionId,
                startedAt: now);
            inactiveAttempt.CompleteFailure("CONNECTION_INACTIVE", err, completedAt);
            dbContext.OutboundDeliveryAttempts.Add(inactiveAttempt);

            message.RecordDeliveryFailure(err, WebhookFailureClassification.Permanent, completedAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            LogDeliveryFailed(logger, message.Id, message.AttemptCount, message.Status, message.TenantId);
            return;
        }

        if (!providerRegistry.TryGetProvider(message.Channel, out var provider) || provider is null)
        {
            var completedAt = timeProvider.UtcNow;
            var err = $"Provider for channel '{message.Channel}' is not registered.";
            var noProviderAttempt = OutboundDeliveryAttempt.Create(
                tenantId: message.TenantId,
                outboundMessageId: message.Id,
                attemptNumber: message.AttemptCount,
                channel: message.Channel,
                connectionId: message.ConnectionId,
                startedAt: now);
            noProviderAttempt.CompleteFailure("PROVIDER_UNAVAILABLE", err, completedAt);
            dbContext.OutboundDeliveryAttempts.Add(noProviderAttempt);

            message.RecordDeliveryFailure(err, WebhookFailureClassification.Permanent, completedAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            LogDeliveryFailed(logger, message.Id, message.AttemptCount, message.Status, message.TenantId);
            return;
        }

        var snapshot = ChannelConnectionSnapshot.Create(
            connectionId: connection.Id,
            tenantId: connection.TenantId,
            storeId: connection.StoreId,
            channel: connection.Channel,
            status: connection.Status,
            externalAccountId: connection.ExternalAccountId,
            encryptedCredentials: connection.EncryptedCredentials,
            capabilities: connection.Capabilities);

        IReadOnlyDictionary<string, string>? metadata = null;
        if (!string.IsNullOrWhiteSpace(message.MetadataJson))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(message.MetadataJson);
            }
            catch
            {
                // ignore deserialization errors on metadata
            }
        }

        var outboundRequest = new OutboundMessageRequest(
            MessageId: message.Id,
            ConversationId: message.ConversationId ?? string.Empty,
            RecipientChannelId: message.RecipientChannelId,
            Text: message.TextContent,
            MediaUrl: message.MediaUrl,
            MediaContentType: message.MediaContentType,
            Caption: message.Caption,
            TemplateCode: message.TemplateCode,
            Metadata: metadata,
            IdempotencyKey: message.IdempotencyKey);

        var attempt = OutboundDeliveryAttempt.Create(
            tenantId: message.TenantId,
            outboundMessageId: message.Id,
            attemptNumber: message.AttemptCount,
            channel: message.Channel,
            connectionId: message.ConnectionId,
            startedAt: now);

        try
        {
            var deliveryResult = await provider.SendMessageAsync(snapshot, outboundRequest, cancellationToken);
            var completedAt = timeProvider.UtcNow;

            if (deliveryResult.Succeeded && !string.IsNullOrWhiteSpace(deliveryResult.ProviderMessageId))
            {
                attempt.CompleteSuccess(deliveryResult.ProviderMessageId, completedAt);
                message.RecordDeliverySuccess(deliveryResult.ProviderMessageId, completedAt);
                LogMessageSent(logger, message.Id, deliveryResult.ProviderMessageId, message.TenantId);
            }
            else
            {
                var code = deliveryResult.ProviderErrorCode ?? "SEND_FAILED";
                var err = deliveryResult.ProviderErrorMessage ?? "Provider returned unsuccessful delivery result.";
                attempt.CompleteFailure(code, err, completedAt);

                var classification = err.Contains("rate limit", StringComparison.OrdinalIgnoreCase) || err.Contains("429", StringComparison.OrdinalIgnoreCase)
                    ? WebhookFailureClassification.Transient
                    : WebhookFailureClassification.Permanent;

                message.RecordDeliveryFailure(err, classification, completedAt);
                LogDeliveryFailed(logger, message.Id, message.AttemptCount, message.Status, message.TenantId);
            }
        }
        catch (Exception ex)
        {
            var completedAt = timeProvider.UtcNow;
            var classification = WebhookFailureClassifier.Classify(ex);
            attempt.CompleteFailure(ex.GetType().Name, ex.Message, completedAt);
            message.RecordDeliveryFailure(ex.Message, classification, completedAt);
            LogDeliveryFailed(logger, message.Id, message.AttemptCount, message.Status, message.TenantId);
        }

        dbContext.OutboundDeliveryAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessStatusReceiptAsync(
        string connectionId,
        string providerMessageId,
        MessageDeliveryStatus status,
        DateTimeOffset? timestamp,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(providerMessageId))
        {
            return;
        }

        var message = await dbContext.OutboundMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.ConnectionId == connectionId && m.ProviderMessageId == providerMessageId, cancellationToken);

        if (message is null) return;

        using var scope = tenantContext.BeginScope(new TenantContext(message.TenantId, null, null, null));
        var ts = timestamp ?? timeProvider.UtcNow;

        message.UpdateProviderStatus(status, ts);
        await dbContext.SaveChangesAsync(cancellationToken);

        LogStatusReceiptUpdated(logger, message.Id, status);
    }

    public async Task<OutboundMessageDto?> GetMessageAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        var message = await dbContext.OutboundMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        return message is null ? null : MapToDto(message);
    }

    public async Task<IReadOnlyList<OutboundDeliveryAttemptDto>> GetDeliveryAttemptsAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        var attempts = await dbContext.OutboundDeliveryAttempts
            .Where(a => a.OutboundMessageId == messageId)
            .OrderBy(a => a.AttemptNumber)
            .ToListAsync(cancellationToken);

        return attempts.Select(a => new OutboundDeliveryAttemptDto(
            a.Id,
            a.OutboundMessageId,
            a.AttemptNumber,
            a.Channel,
            a.StartedAt,
            a.CompletedAt,
            a.Succeeded,
            a.ProviderMessageId,
            a.ProviderErrorCode,
            a.ProviderErrorMessage)).ToList();
    }

    public async Task<PagedResult<OutboundMessageDto>> GetMessagesAsync(
        OutboundMessageQuery query,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        var q = dbContext.OutboundMessages.AsNoTracking();

        if (query.Status.HasValue)
        {
            q = q.Where(m => m.Status == query.Status.Value);
        }

        if (query.Channel.HasValue)
        {
            q = q.Where(m => m.Channel == query.Channel.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.ConnectionId))
        {
            q = q.Where(m => m.ConnectionId == query.ConnectionId);
        }

        var totalCount = await q.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var items = await q
            .OrderByDescending(m => m.QueuedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<OutboundMessageDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<OutboundMessageResult> CancelMessageAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        _ = tenantContext.RequireCurrent();

        var message = await dbContext.OutboundMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message is null)
        {
            return OutboundMessageResult.Failed($"Message '{messageId}' was not found.");
        }

        try
        {
            message.Cancel(timeProvider.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return OutboundMessageResult.Success(message.Id, message.Status);
        }
        catch (InvalidOperationException ex)
        {
            return OutboundMessageResult.Failed(ex.Message);
        }
    }

    public async Task<OutboundMessageResult> ReplayMessageAsync(
        string messageId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        var context = tenantContext.RequireCurrent();

        var message = await dbContext.OutboundMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message is null)
        {
            return OutboundMessageResult.Failed($"Message '{messageId}' was not found.");
        }

        try
        {
            message.Replay(timeProvider.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEvents.AppendAsync(
                new AuditEventWrite(
                    Action: "integrations.message.replayed",
                    TargetType: "outbound-message",
                    TargetId: message.Id,
                    Metadata: JsonSerializer.Serialize(new
                    {
                        idempotencyKey,
                        channel = message.Channel.ToString(),
                        connectionId = message.ConnectionId
                    })),
                cancellationToken);

            return OutboundMessageResult.Success(message.Id, message.Status);
        }
        catch (InvalidOperationException ex)
        {
            return OutboundMessageResult.Failed(ex.Message);
        }
    }

    public async Task<PagedResult<OutboundDeadLetterDto>> GetDeadLetterMessagesAsync(
        OutboundDeadLetterQuery query,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        var q = dbContext.OutboundMessages.AsNoTracking()
            .Where(m => m.Status == OutboundMessageStatus.DeadLetter);

        if (query.Channel.HasValue)
        {
            q = q.Where(m => m.Channel == query.Channel.Value);
        }

        var totalCount = await q.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var items = await q
            .OrderByDescending(m => m.DeadLetteredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(m => new OutboundDeadLetterDto(
            m.Id,
            m.TenantId,
            m.ConnectionId,
            m.Channel,
            m.RecipientChannelId,
            m.IdempotencyKey,
            m.MessageType,
            m.TextContent ?? m.MediaUrl ?? m.TemplateCode,
            m.LastErrorMessage,
            m.FailureClassification,
            m.AttemptCount,
            m.MaxAttempts,
            m.QueuedAt,
            m.DeadLetteredAt)).ToList();

        return new PagedResult<OutboundDeadLetterDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    private static OutboundMessageDto MapToDto(OutboundMessage m) =>
        new(
            m.Id,
            m.TenantId,
            m.ConnectionId,
            m.Channel,
            m.ConversationId,
            m.RecipientChannelId,
            m.IdempotencyKey,
            m.MessageType,
            m.TextContent,
            m.MediaUrl,
            m.MediaContentType,
            m.Caption,
            m.TemplateCode,
            m.TemplateParametersJson,
            m.MetadataJson,
            m.Status,
            m.ProviderMessageId,
            m.AttemptCount,
            m.MaxAttempts,
            m.NextRetryAt,
            m.FailureClassification,
            m.LastErrorMessage,
            m.QueuedAt,
            m.SentAt,
            m.DeliveredAt,
            m.ReadAt,
            m.FailedAt,
            m.DeadLetteredAt,
            m.CancelledAt);
}
