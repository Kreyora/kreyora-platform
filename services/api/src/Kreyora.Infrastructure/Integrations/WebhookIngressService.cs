using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Integrations;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.Integrations;

public sealed partial class WebhookIngressService(
    AppDbContext dbContext,
    IChannelProviderRegistry providerRegistry,
    ISecretEncryptionService encryptionService,
    ITenantContextAccessor tenantContext,
    ILogger<WebhookIngressService> logger) : IWebhookIngressService
{
    public const int MaxPayloadSizeBytes = 256 * 1024; // 256 KB

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook payload exceeds maximum allowed size ({Size} bytes). Correlation: {CorrelationId}")]
    private static partial void LogPayloadTooLarge(ILogger logger, int size, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook content-type '{ContentType}' is unsupported. Correlation: {CorrelationId}")]
    private static partial void LogUnsupportedMediaType(ILogger logger, string? contentType, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Channel provider for '{Channel}' not found. Correlation: {CorrelationId}")]
    private static partial void LogProviderNotFound(ILogger logger, ChannelType channel, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection '{ConnectionId}' not found. Correlation: {CorrelationId}")]
    private static partial void LogConnectionNotFound(ILogger logger, string? connectionId, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No channel connection found for channel '{Channel}' and account '{AccountId}'. Correlation: {CorrelationId}")]
    private static partial void LogAccountNotFound(ILogger logger, ChannelType channel, string? accountId, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Channel connection '{ConnectionId}' is not active (status: {Status}). Correlation: {CorrelationId}")]
    private static partial void LogConnectionNotActive(ILogger logger, string connectionId, ChannelConnectionStatus status, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to decrypt credentials for connection '{ConnectionId}'. Correlation: {CorrelationId}")]
    private static partial void LogDecryptionFailed(ILogger logger, Exception ex, string connectionId, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook validation failed for connection '{ConnectionId}'. Reason: {Reason}. Correlation: {CorrelationId}")]
    private static partial void LogValidationFailed(ILogger logger, string connectionId, string? reason, string correlationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate webhook event {ProviderEventId} detected for connection {ConnectionId}. Acknowledged in {ElapsedMs}ms.")]
    private static partial void LogDuplicateDetected(ILogger logger, string providerEventId, string connectionId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook event {EventId} (provider: {ProviderEventId}) persisted for connection {ConnectionId} in {ElapsedMs}ms. Correlation: {CorrelationId}")]
    private static partial void LogEventPersisted(ILogger logger, string eventId, string providerEventId, string connectionId, long elapsedMs, string correlationId);

    public async Task<WebhookIngressResult> HandleWebhookAsync(
        WebhookIngressCommand command,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = command.CorrelationId ?? Guid.NewGuid().ToString("N");

        // 1. Enforce payload size limits
        if (command.RawBody.Length > MaxPayloadSizeBytes)
        {
            LogPayloadTooLarge(logger, command.RawBody.Length, correlationId);
            return WebhookIngressResult.PayloadTooLarge();
        }

        // 2. Enforce Content-Type limits
        if (!string.IsNullOrEmpty(command.ContentType) &&
            !command.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            LogUnsupportedMediaType(logger, command.ContentType, correlationId);
            return WebhookIngressResult.UnsupportedMediaType(command.ContentType);
        }

        // 3. Resolve channel provider
        if (!providerRegistry.TryGetProvider(command.Channel, out var provider))
        {
            LogProviderNotFound(logger, command.Channel, correlationId);
            return WebhookIngressResult.NotFound($"Provider for channel '{command.Channel}' not found");
        }

        // 4. Resolve connection
        ChannelConnection? connection = null;
        if (!string.IsNullOrWhiteSpace(command.ConnectionId))
        {
            connection = await dbContext.ChannelConnections
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == command.ConnectionId, cancellationToken);

            if (connection == null)
            {
                LogConnectionNotFound(logger, command.ConnectionId, correlationId);
                return WebhookIngressResult.NotFound($"ChannelConnection '{command.ConnectionId}' not found");
            }
        }
        else
        {
            // Try resolving by external account ID from header or body
            var externalAccountId = command.Headers.GetValueOrDefault("X-External-Account-Id") ??
                                    command.Headers.GetValueOrDefault("X-Account-Id");

            if (string.IsNullOrWhiteSpace(externalAccountId))
            {
                try
                {
                    using var doc = JsonDocument.Parse(command.RawBody);
                    if (doc.RootElement.TryGetProperty("account_id", out var accProp))
                    {
                        externalAccountId = accProp.GetString();
                    }
                    else if (doc.RootElement.TryGetProperty("external_account_id", out var extProp))
                    {
                        externalAccountId = extProp.GetString();
                    }
                }
                catch
                {
                    // ignore JSON parse error here
                }
            }

            if (!string.IsNullOrWhiteSpace(externalAccountId))
            {
                connection = await dbContext.ChannelConnections
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Channel == command.Channel && c.ExternalAccountId == externalAccountId, cancellationToken);
            }

            if (connection == null)
            {
                LogAccountNotFound(logger, command.Channel, externalAccountId ?? "unknown", correlationId);
                return WebhookIngressResult.NotFound($"No channel connection found for channel '{command.Channel}'");
            }
        }

        // 5. Verify connection is active
        if (connection.Status != ChannelConnectionStatus.Active)
        {
            LogConnectionNotActive(logger, connection.Id, connection.Status, correlationId);
            return WebhookIngressResult.Forbidden($"Channel connection '{connection.Id}' is not active");
        }

        // 6. Decrypt secret for validation
        string? secret = null;
        if (connection.EncryptedCredentials != null)
        {
            try
            {
                secret = encryptionService.Decrypt(connection.EncryptedCredentials);
            }
            catch (Exception ex)
            {
                LogDecryptionFailed(logger, ex, connection.Id, correlationId);
                return WebhookIngressResult.InternalError("Credential decryption failed");
            }
        }
        else if (!string.IsNullOrEmpty(connection.WebhookVerificationToken))
        {
            secret = connection.WebhookVerificationToken;
        }

        // 7. Cryptographic signature and replay window validation
        var validationReq = new WebhookValidationRequest(
            command.Method,
            command.Path,
            command.Headers,
            command.QueryParameters,
            command.RawBody,
            secret);

        var validationResult = await provider.ValidateWebhookAsync(validationReq, cancellationToken);
        if (!validationResult.IsValid)
        {
            LogValidationFailed(logger, connection.Id, validationResult.FailureReason, correlationId);

            if (validationResult.FailureReason?.Contains("replay", StringComparison.OrdinalIgnoreCase) == true)
            {
                return WebhookIngressResult.ReplayWindowExpired(validationResult.FailureReason);
            }

            return WebhookIngressResult.InvalidSignature(validationResult.FailureReason ?? "Invalid signature");
        }

        // 8. Extract ProviderEventId
        var providerEventId = validationResult.ProviderEventId ??
                              command.Headers.GetValueOrDefault("X-Provider-Event-Id") ??
                              command.Headers.GetValueOrDefault("X-Event-Id");

        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            providerEventId = "evt_" + Guid.NewGuid().ToString("N");
        }

        // 9. Deduplication check
        var existingEvent = await dbContext.WebhookEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.ConnectionId == connection.Id && e.ProviderEventId == providerEventId, cancellationToken);

        if (existingEvent != null)
        {
            sw.Stop();
            LogDuplicateDetected(logger, providerEventId, connection.Id, sw.ElapsedMilliseconds);
            return WebhookIngressResult.Success(existingEvent.Id, isDuplicate: true);
        }

        // 10. Durable persistence
        var headersJson = JsonSerializer.Serialize(command.Headers);
        var rawBodyString = Encoding.UTF8.GetString(command.RawBody);
        var occurredAt = command.ReceivedAt;

        var webhookEvent = WebhookEvent.Create(
            tenantId: connection.TenantId,
            connectionId: connection.Id,
            channel: command.Channel,
            providerEventId: providerEventId,
            eventType: null,
            occurredAt: occurredAt,
            receivedAt: command.ReceivedAt,
            correlationId: correlationId,
            headers: headersJson,
            rawPayload: rawBodyString);

        try
        {
            using (tenantContext.BeginScope(new TenantContext(connection.TenantId, null, null, null)))
            {
                dbContext.WebhookEvents.Add(webhookEvent);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            // Concurrent insert race condition: re-query existing event to return idempotent duplicate success
            var duplicateEvent = await dbContext.WebhookEvents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.ConnectionId == connection.Id && e.ProviderEventId == providerEventId, cancellationToken);

            if (duplicateEvent != null)
            {
                sw.Stop();
                LogDuplicateDetected(logger, providerEventId, connection.Id, sw.ElapsedMilliseconds);
                return WebhookIngressResult.Success(duplicateEvent.Id, isDuplicate: true);
            }

            throw;
        }

        sw.Stop();
        LogEventPersisted(logger, webhookEvent.Id, providerEventId, connection.Id, sw.ElapsedMilliseconds, correlationId);

        return WebhookIngressResult.Success(webhookEvent.Id, isDuplicate: false);
    }

    public async Task<WebhookChallengeResult> HandleChallengeAsync(
        WebhookChallengeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!providerRegistry.TryGetProvider(command.Channel, out var provider))
        {
            return WebhookChallengeResult.NotFound($"Provider for channel '{command.Channel}' not found");
        }

        string? secret = null;
        if (!string.IsNullOrWhiteSpace(command.ConnectionId))
        {
            var connection = await dbContext.ChannelConnections
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == command.ConnectionId, cancellationToken);

            if (connection == null)
            {
                return WebhookChallengeResult.NotFound($"ChannelConnection '{command.ConnectionId}' not found");
            }

            if (connection.EncryptedCredentials != null)
            {
                secret = encryptionService.Decrypt(connection.EncryptedCredentials);
            }
            else if (!string.IsNullOrEmpty(connection.WebhookVerificationToken))
            {
                secret = connection.WebhookVerificationToken;
            }
        }

        var validationReq = new WebhookValidationRequest(
            "GET",
            "/webhook",
            command.Headers,
            command.QueryParameters,
            Array.Empty<byte>(),
            secret);

        var result = await provider.ValidateWebhookAsync(validationReq, cancellationToken);
        if (result.IsValid && !string.IsNullOrEmpty(result.ChallengeResponse))
        {
            return WebhookChallengeResult.Valid(result.ChallengeResponse);
        }

        return WebhookChallengeResult.Invalid(result.FailureReason ?? "Challenge verification failed");
    }
}
