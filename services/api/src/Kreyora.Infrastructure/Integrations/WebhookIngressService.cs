using System.Diagnostics;
using System.Security.Cryptography;
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
    ILogger<WebhookIngressService> logger,
    IIntegrationWorkScheduler? workScheduler = null) : IWebhookIngressService
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Signed webhook account does not match connection '{ConnectionId}'. Correlation: {CorrelationId}")]
    private static partial void LogAccountMismatch(ILogger logger, string connectionId, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verified '{Channel}' webhook entries for unknown account (hash {AccountHash}) acknowledged and dropped. Correlation: {CorrelationId}")]
    private static partial void LogUnknownAccountIgnored(ILogger logger, ChannelType channel, string accountHash, string correlationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Verified webhook for connection '{ConnectionId}' in status {Status} acknowledged and not stored. Correlation: {CorrelationId}")]
    private static partial void LogConnectionStatusIgnored(ILogger logger, string connectionId, ChannelConnectionStatus status, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verified '{Channel}' webhook had no routable account entries; acknowledged and dropped. Correlation: {CorrelationId}")]
    private static partial void LogUnroutableIgnored(ILogger logger, ChannelType channel, string correlationId);

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

        if (!provider.UsesConnectionSecretForSignature)
        {
            return await HandleAppSignedWebhookAsync(provider, command, correlationId, sw, cancellationToken);
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
            // Signed payload identity first (provider hook), then routing headers, then legacy body fields.
            var externalAccountId = ResolveLegacyExternalAccountId(provider, command);

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

        // 7b. The account named by the validated request must be the resolved connection's account (ADR-015).
        if (!string.IsNullOrWhiteSpace(validationResult.ExternalAccountId)
            && !string.Equals(validationResult.ExternalAccountId, connection.ExternalAccountId, StringComparison.Ordinal))
        {
            LogAccountMismatch(logger, connection.Id, correlationId);
            return WebhookIngressResult.AccountMismatch("Webhook account does not match the addressed connection");
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
        workScheduler?.ScheduleWebhookProcessing(webhookEvent.Id);

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

            // M07 semantics. App-level providers (Instagram) ignore this and use their own verify token.
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

    /// <summary>
    /// App-signed providers (ADR-015): authenticate the whole delivery with the app secret before reading
    /// any connection data, split it per provider account, route each slice through globally unique
    /// account ownership, apply the connection-status policy, and persist all slices atomically.
    /// </summary>
    private async Task<WebhookIngressResult> HandleAppSignedWebhookAsync(
        IChannelProvider provider,
        WebhookIngressCommand command,
        string correlationId,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        var ackStatus = provider.AcknowledgementStatusCode;

        var validationResult = await provider.ValidateWebhookAsync(
            new WebhookValidationRequest(
                command.Method,
                command.Path,
                command.Headers,
                command.QueryParameters,
                command.RawBody,
                Secret: null),
            cancellationToken);

        if (!validationResult.IsValid)
        {
            LogValidationFailed(logger, command.ConnectionId ?? "(app)", validationResult.FailureReason, correlationId);
            if (validationResult.FailureReason?.Contains("replay", StringComparison.OrdinalIgnoreCase) == true)
            {
                return WebhookIngressResult.ReplayWindowExpired(validationResult.FailureReason);
            }

            return WebhookIngressResult.InvalidSignature(validationResult.FailureReason ?? "Invalid signature");
        }

        var providerEventId = validationResult.ProviderEventId ?? "evt_" + Guid.NewGuid().ToString("N");
        var rawBodyString = Encoding.UTF8.GetString(command.RawBody);
        var slices = SplitVerifiedBody(provider, command.RawBody);

        var targets = new List<(ChannelConnection Connection, string RawBody)>();
        if (!string.IsNullOrWhiteSpace(command.ConnectionId))
        {
            var addressed = await dbContext.ChannelConnections
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == command.ConnectionId && c.Channel == command.Channel, cancellationToken);

            if (addressed == null)
            {
                LogConnectionNotFound(logger, command.ConnectionId, correlationId);
                return WebhookIngressResult.NotFound($"ChannelConnection '{command.ConnectionId}' not found");
            }

            if (slices.Count == 0)
            {
                // Signed but unparseable/entry-less body addressed to one connection: keep it for the DLQ.
                targets.Add((addressed, rawBodyString));
            }
            else if (slices.Any(slice => !string.Equals(slice.ExternalAccountId, addressed.ExternalAccountId, StringComparison.Ordinal)))
            {
                LogAccountMismatch(logger, addressed.Id, correlationId);
                return WebhookIngressResult.AccountMismatch("Webhook account does not match the addressed connection");
            }
            else
            {
                targets.AddRange(slices.Select(slice => (addressed, slice.RawBody)));
            }
        }
        else
        {
            if (slices.Count == 0)
            {
                LogUnroutableIgnored(logger, command.Channel, correlationId);
                return WebhookIngressResult.Ignored(ackStatus, "No routable account entries");
            }

            var accounts = slices.Select(slice => slice.ExternalAccountId).Distinct(StringComparer.Ordinal).ToList();
            var owners = await dbContext.ChannelConnections
                .IgnoreQueryFilters()
                .Where(c => c.Channel == command.Channel && accounts.Contains(c.ExternalAccountId))
                .ToListAsync(cancellationToken);

            foreach (var slice in slices)
            {
                var owner = owners.FirstOrDefault(c => string.Equals(c.ExternalAccountId, slice.ExternalAccountId, StringComparison.Ordinal));
                if (owner == null)
                {
                    LogUnknownAccountIgnored(logger, command.Channel, HashForLog(slice.ExternalAccountId), correlationId);
                    continue;
                }

                targets.Add((owner, slice.RawBody));
            }
        }

        // Connection-status policy (owner decision 2026-10-05): inbound customer messages are kept while a
        // seller repairs credentials; deliberately disabled, revoked, or never-activated connections store nothing.
        var accepted = new List<(ChannelConnection Connection, string RawBody)>();
        foreach (var target in targets)
        {
            if (AcceptsInbound(target.Connection.Status))
            {
                accepted.Add(target);
            }
            else
            {
                LogConnectionStatusIgnored(logger, target.Connection.Id, target.Connection.Status, correlationId);
            }
        }

        if (accepted.Count == 0)
        {
            return WebhookIngressResult.Ignored(ackStatus, "No accepting connection for this delivery");
        }

        var connectionIds = accepted.Select(t => t.Connection.Id).Distinct(StringComparer.Ordinal).ToList();
        var existing = await FindExistingEventsAsync(connectionIds, providerEventId, cancellationToken);
        var fresh = accepted.Where(t => !existing.ContainsKey(t.Connection.Id)).ToList();
        if (fresh.Count == 0)
        {
            sw.Stop();
            LogDuplicateDetected(logger, providerEventId, connectionIds[0], sw.ElapsedMilliseconds);
            return WebhookIngressResult.Duplicate(existing[connectionIds[0]], ackStatus);
        }

        var headersJson = JsonSerializer.Serialize(command.Headers);
        var created = fresh
            .Select(t => WebhookEvent.Create(
                tenantId: t.Connection.TenantId,
                connectionId: t.Connection.Id,
                channel: command.Channel,
                providerEventId: providerEventId,
                eventType: null,
                occurredAt: command.ReceivedAt,
                receivedAt: command.ReceivedAt,
                correlationId: correlationId,
                headers: headersJson,
                rawPayload: t.RawBody))
            .ToList();

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            foreach (var tenantGroup in created.GroupBy(e => e.TenantId, StringComparer.Ordinal))
            {
                using (tenantContext.BeginScope(new TenantContext(tenantGroup.Key, null, null, null)))
                {
                    dbContext.WebhookEvents.AddRange(tenantGroup);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent identical delivery won the unique index: the transaction rolled back; answer idempotently.
            foreach (var ev in created)
            {
                dbContext.Entry(ev).State = EntityState.Detached;
            }

            var raced = await FindExistingEventsAsync(connectionIds, providerEventId, cancellationToken);
            if (connectionIds.All(raced.ContainsKey))
            {
                sw.Stop();
                LogDuplicateDetected(logger, providerEventId, connectionIds[0], sw.ElapsedMilliseconds);
                return WebhookIngressResult.Duplicate(raced[connectionIds[0]], ackStatus);
            }

            throw;
        }

        sw.Stop();
        foreach (var ev in created)
        {
            LogEventPersisted(logger, ev.Id, providerEventId, ev.ConnectionId, sw.ElapsedMilliseconds, correlationId);
            workScheduler?.ScheduleWebhookProcessing(ev.Id);
        }

        return WebhookIngressResult.Accepted(created[0].Id, ackStatus);
    }

    private async Task<Dictionary<string, string>> FindExistingEventsAsync(
        IReadOnlyCollection<string> connectionIds,
        string providerEventId,
        CancellationToken cancellationToken) =>
        await dbContext.WebhookEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => connectionIds.Contains(e.ConnectionId) && e.ProviderEventId == providerEventId)
            .ToDictionaryAsync(e => e.ConnectionId, e => e.Id, StringComparer.Ordinal, cancellationToken);

    private static IReadOnlyList<WebhookAccountSlice> SplitVerifiedBody(IChannelProvider provider, byte[] rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var slices = provider.SplitByAccount(document);
            if (slices != null)
            {
                return slices;
            }

            var accountId = provider.ResolveExternalAccountId(document);
            return string.IsNullOrWhiteSpace(accountId)
                ? Array.Empty<WebhookAccountSlice>()
                : [new WebhookAccountSlice(accountId, Encoding.UTF8.GetString(rawBody))];
        }
        catch (JsonException)
        {
            return Array.Empty<WebhookAccountSlice>();
        }
    }

    private static string? ResolveLegacyExternalAccountId(IChannelProvider provider, WebhookIngressCommand command)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(command.RawBody);
        }
        catch (JsonException)
        {
            // Unparseable body: only routing headers can identify the account.
        }

        using (document)
        {
            if (document != null && provider.ResolveExternalAccountId(document) is { Length: > 0 } signedAccount)
            {
                return signedAccount;
            }

            var headerAccount = command.Headers.GetValueOrDefault("X-External-Account-Id") ??
                                command.Headers.GetValueOrDefault("X-Account-Id");
            if (!string.IsNullOrWhiteSpace(headerAccount))
            {
                return headerAccount;
            }

            if (document?.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("account_id", out var accProp) && accProp.ValueKind == JsonValueKind.String)
                {
                    return accProp.GetString();
                }

                if (document.RootElement.TryGetProperty("external_account_id", out var extProp) && extProp.ValueKind == JsonValueKind.String)
                {
                    return extProp.GetString();
                }
            }

            return null;
        }
    }

    private static bool AcceptsInbound(ChannelConnectionStatus status) =>
        status is ChannelConnectionStatus.Active or ChannelConnectionStatus.Degraded or ChannelConnectionStatus.Expired;

    private static string HashForLog(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
}
