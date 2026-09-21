using System.Text;
using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Integrations.Simulator;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kreyora.Infrastructure.Integrations;

public sealed partial class IntegrationDiagnosticsService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    IAuditEventService auditEvents,
    IWebhookIngressService ingressService,
    IWebhookProcessingService processingService,
    IChannelConnectionService connectionService,
    IOutboundMessageService outboundMessageService,
    ILogger<IntegrationDiagnosticsService> logger) : IIntegrationDiagnosticsService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Executing simulator scenario {Scenario} for connection {ConnectionId} in tenant {TenantId}")]
    private static partial void LogScenarioExecuting(ILogger logger, SimulatorScenarioType scenario, string connectionId, string tenantId);

    public async Task<Result<IntegrationOverviewDto>> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        var connections = dbContext.ChannelConnections.AsNoTracking();
        var totalConnections = await connections.CountAsync(cancellationToken);
        var activeConnections = await connections.CountAsync(c => c.Status == ChannelConnectionStatus.Active, cancellationToken);
        var degradedConnections = await connections.CountAsync(c => c.Status == ChannelConnectionStatus.Degraded, cancellationToken);
        var expiredConnections = await connections.CountAsync(c => c.Status == ChannelConnectionStatus.Expired, cancellationToken);

        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);

        var eventsProcessed24h = await dbContext.WebhookEvents.AsNoTracking()
            .CountAsync(e => e.ProcessedAt >= cutoff && e.ProcessingStatus == WebhookProcessingStatus.Processed, cancellationToken);

        var eventsFailed24h = await dbContext.WebhookEvents.AsNoTracking()
            .CountAsync(e => e.LastAttemptedAt >= cutoff &&
                (e.ProcessingStatus == WebhookProcessingStatus.Failed || e.ProcessingStatus == WebhookProcessingStatus.DeadLetter), cancellationToken);

        var inboundDeadLetterCount = await dbContext.WebhookEvents.AsNoTracking()
            .CountAsync(e => e.ProcessingStatus == WebhookProcessingStatus.DeadLetter, cancellationToken);

        var outboundDeadLetterCount = await dbContext.OutboundMessages.AsNoTracking()
            .CountAsync(m => m.Status == OutboundMessageStatus.DeadLetter, cancellationToken);

        return Result<IntegrationOverviewDto>.Success(new IntegrationOverviewDto(
            TotalConnections: totalConnections,
            ActiveConnections: activeConnections,
            DegradedConnections: degradedConnections,
            ExpiredConnections: expiredConnections,
            EventsProcessed24h: eventsProcessed24h,
            EventsFailed24h: eventsFailed24h,
            InboundDeadLetterCount: inboundDeadLetterCount,
            OutboundDeadLetterCount: outboundDeadLetterCount,
            ComputedAt: DateTimeOffset.UtcNow));
    }

    public async Task<Result<ConnectionDiagnosticsDto>> GetConnectionDiagnosticsAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        var connection = await dbContext.ChannelConnections.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection is null)
        {
            return Result<ConnectionDiagnosticsDto>.NotFound("Channel connection not found.");
        }

        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);

        var eventsProcessed24h = await dbContext.WebhookEvents.AsNoTracking()
            .CountAsync(e => e.ConnectionId == connectionId && e.ProcessedAt >= cutoff && e.ProcessingStatus == WebhookProcessingStatus.Processed, cancellationToken);

        var eventsFailed24h = await dbContext.WebhookEvents.AsNoTracking()
            .CountAsync(e => e.ConnectionId == connectionId && e.LastAttemptedAt >= cutoff &&
                (e.ProcessingStatus == WebhookProcessingStatus.Failed || e.ProcessingStatus == WebhookProcessingStatus.DeadLetter), cancellationToken);

        var deadLetterCount = await dbContext.WebhookEvents.AsNoTracking()
            .CountAsync(e => e.ConnectionId == connectionId && e.ProcessingStatus == WebhookProcessingStatus.DeadLetter, cancellationToken);

        var lastEventAt = await dbContext.WebhookEvents.AsNoTracking()
            .Where(e => e.ConnectionId == connectionId)
            .OrderByDescending(e => e.ReceivedAt)
            .Select(e => (DateTimeOffset?)e.ReceivedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var webhookUrl = $"/v1/webhooks/{connection.Channel.ToString().ToLowerInvariant()}/{connection.Id}";

        return Result<ConnectionDiagnosticsDto>.Success(new ConnectionDiagnosticsDto(
            ConnectionId: connection.Id,
            Channel: connection.Channel,
            DisplayName: connection.DisplayName,
            Status: connection.Status,
            IsHealthy: connection.Status == ChannelConnectionStatus.Active,
            HealthSummary: connection.HealthSummary,
            LastErrorMessage: connection.HealthDetails,
            LastHealthCheckAt: connection.LastHealthCheckAt,
            TokenExpiresAt: connection.TokenExpiresAt,
            WebhookUrl: webhookUrl,
            EventsProcessed24h: eventsProcessed24h,
            EventsFailed24h: eventsFailed24h,
            DeadLetterCount: deadLetterCount,
            LastEventAt: lastEventAt));
    }

    public async Task<Result<PagedResult<WebhookEventDto>>> GetConnectionWebhooksAsync(
        string connectionId,
        int page = 1,
        int pageSize = 20,
        WebhookProcessingStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var connectionExists = await dbContext.ChannelConnections.AsNoTracking()
            .AnyAsync(c => c.Id == connectionId, cancellationToken);

        if (!connectionExists)
        {
            return Result<PagedResult<WebhookEventDto>>.NotFound("Channel connection not found.");
        }

        var query = dbContext.WebhookEvents.AsNoTracking()
            .Where(e => e.ConnectionId == connectionId);

        if (status.HasValue)
        {
            query = query.Where(e => e.ProcessingStatus == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(e => e.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new WebhookEventDto(
                e.Id,
                e.ConnectionId,
                e.Channel,
                e.ProviderEventId,
                e.EventType,
                e.ProcessingStatus,
                e.AttemptCount,
                e.MaxAttempts,
                e.FailureClassification,
                e.ErrorMessage,
                e.OccurredAt,
                e.ReceivedAt,
                e.ProcessedAt,
                e.DeadLetteredAt))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<WebhookEventDto>>.Success(new PagedResult<WebhookEventDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<Result<WebhookEventDetailDto>> GetWebhookDetailAsync(
        string webhookEventId,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        var current = tenantContext.RequireCurrent();

        var ev = await dbContext.WebhookEvents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == webhookEventId, cancellationToken);

        if (ev is null)
        {
            return Result<WebhookEventDetailDto>.NotFound("Webhook event not found.");
        }

        // Role-based redaction per ADR-012: Only Owner and PlatformSupport view raw payload
        var canViewRawPayload = current.Role == TenantRole.Owner || current.IsReadOnlySupport;
        var rawPayload = canViewRawPayload ? ev.RawPayload : "[REDACTED]";
        var isRedacted = !canViewRawPayload;

        return Result<WebhookEventDetailDto>.Success(new WebhookEventDetailDto(
            Id: ev.Id,
            ConnectionId: ev.ConnectionId,
            Channel: ev.Channel,
            ProviderEventId: ev.ProviderEventId,
            EventType: ev.EventType,
            Status: ev.ProcessingStatus,
            AttemptCount: ev.AttemptCount,
            MaxAttempts: ev.MaxAttempts,
            FailureClassification: ev.FailureClassification,
            ErrorMessage: ev.ErrorMessage,
            OccurredAt: ev.OccurredAt,
            ReceivedAt: ev.ReceivedAt,
            ProcessedAt: ev.ProcessedAt,
            DeadLetteredAt: ev.DeadLetteredAt,
            RawPayload: rawPayload,
            IsPayloadRedacted: isRedacted,
            CorrelationId: ev.CorrelationId));
    }

    public async Task<Result<SimulatorScenarioResult>> ExecuteSimulatorScenarioAsync(
        SimulatorScenarioRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        var current = tenantContext.RequireCurrent();

        var connection = await dbContext.ChannelConnections
            .SingleOrDefaultAsync(c => c.Id == request.ConnectionId, cancellationToken);

        if (connection is null)
        {
            return Result<SimulatorScenarioResult>.NotFound("Channel connection not found.");
        }

        if (connection.Channel != ChannelType.Simulator)
        {
            return Result<SimulatorScenarioResult>.ValidationError("Simulator scenarios can only be executed against Simulator connections.");
        }

        LogScenarioExecuting(logger, request.Scenario, request.ConnectionId, current.TenantId);

        var now = DateTimeOffset.UtcNow;
        var correlationId = "sim_corr_" + Guid.NewGuid().ToString("N")[..12];
        var generatedId = (string?)null;
        var summary = "";

        switch (request.Scenario)
        {
            case SimulatorScenarioType.HealthyInbound:
            {
                var body = request.CustomPayload ?? JsonSerializer.Serialize(new
                {
                    type = "text",
                    message_id = "sim_msg_" + Guid.NewGuid().ToString("N")[..8],
                    sender_id = "+9779800000999",
                    text = "Namaste from deterministic simulator scenario!"
                });

                var signedReq = SimulatorChannelProvider.CreateSignedWebhookRequest(
                    rawBody: body,
                    secret: connection.WebhookVerificationToken,
                    latencyMs: request.LatencyMs);

                var ingressCmd = new WebhookIngressCommand(
                    Channel: ChannelType.Simulator,
                    ConnectionId: connection.Id,
                    Method: signedReq.Method,
                    Path: signedReq.Path,
                    Headers: signedReq.Headers,
                    QueryParameters: signedReq.QueryParameters,
                    RawBody: signedReq.RawBody,
                    ContentType: "application/json",
                    CorrelationId: correlationId,
                    ReceivedAt: now);

                var ingressResult = await ingressService.HandleWebhookAsync(ingressCmd, cancellationToken);
                generatedId = ingressResult.EventId;

                if (ingressResult.IsSuccess && generatedId is not null)
                {
                    await processingService.ProcessWebhookEventAsync(generatedId, cancellationToken);
                }

                summary = $"Ingressed healthy webhook event {generatedId} and normalized successfully.";
                break;
            }

            case SimulatorScenarioType.DuplicateDelivery:
            {
                var fixedProviderId = "evt_sim_fixed_dup_" + Guid.NewGuid().ToString("N")[..8];
                var body = JsonSerializer.Serialize(new
                {
                    id = fixedProviderId,
                    type = "text",
                    text = "Duplicate delivery scenario test message."
                });

                var signedReq = SimulatorChannelProvider.CreateSignedWebhookRequest(
                    rawBody: body,
                    secret: connection.WebhookVerificationToken,
                    eventId: fixedProviderId);

                var ingressCmd = new WebhookIngressCommand(
                    Channel: ChannelType.Simulator,
                    ConnectionId: connection.Id,
                    Method: signedReq.Method,
                    Path: signedReq.Path,
                    Headers: signedReq.Headers,
                    QueryParameters: signedReq.QueryParameters,
                    RawBody: signedReq.RawBody,
                    ContentType: "application/json",
                    CorrelationId: correlationId,
                    ReceivedAt: now);

                var res1 = await ingressService.HandleWebhookAsync(ingressCmd, cancellationToken);
                var res2 = await ingressService.HandleWebhookAsync(ingressCmd, cancellationToken);

                generatedId = res1.EventId;
                summary = $"First delivery event: {res1.EventId} (status: {res1.StatusCode}); second delivery deduplicated: IsDuplicate={res2.IsDuplicate} (status: {res2.StatusCode}).";
                break;
            }

            case SimulatorScenarioType.OutOfOrderDelivery:
            {
                var pastTime = now.AddMinutes(-10);
                var body = JsonSerializer.Serialize(new
                {
                    type = "text",
                    message_id = "msg_ooo_" + Guid.NewGuid().ToString("N")[..8],
                    occurred_at = pastTime.ToString("o"),
                    text = "Out-of-order delivery with past timestamp."
                });

                var signedReq = SimulatorChannelProvider.CreateSignedWebhookRequest(
                    rawBody: body,
                    secret: connection.WebhookVerificationToken,
                    timestamp: now); // Header timestamp is current so replay-window check passes, but payload occurred_at is 10 min ago

                var ingressCmd = new WebhookIngressCommand(
                    Channel: ChannelType.Simulator,
                    ConnectionId: connection.Id,
                    Method: signedReq.Method,
                    Path: signedReq.Path,
                    Headers: signedReq.Headers,
                    QueryParameters: signedReq.QueryParameters,
                    RawBody: signedReq.RawBody,
                    ContentType: "application/json",
                    CorrelationId: correlationId,
                    ReceivedAt: now);

                var res = await ingressService.HandleWebhookAsync(ingressCmd, cancellationToken);
                generatedId = res.EventId;
                if (res.IsSuccess && generatedId is not null)
                {
                    await processingService.ProcessWebhookEventAsync(generatedId, cancellationToken);
                }
                summary = $"Processed out-of-order event with preserved past occurred_at timestamp ({pastTime:s}).";
                break;
            }

            case SimulatorScenarioType.RateLimit429:
            {
                var queueRes = await outboundMessageService.QueueMessageAsync(new QueueOutboundMessageRequest(
                    ConnectionId: connection.Id,
                    RecipientChannelId: "+9779800000429",
                    IdempotencyKey: "rl_scenario_" + Guid.NewGuid().ToString("N")[..8],
                    MessageType: OutboundMessageType.Text,
                    TextContent: "Testing rate limit feedback [SIMULATE_RATE_LIMIT].",
                    MaxAttempts: 3), cancellationToken);

                generatedId = queueRes.OutboundMessageId;
                if (queueRes.Succeeded && generatedId is not null)
                {
                    await outboundMessageService.ProcessDeliveryAsync(generatedId, cancellationToken);
                }

                summary = $"Outbound message {generatedId} triggered simulated 429 rate limit and scheduled retry.";
                break;
            }

            case SimulatorScenarioType.TransientFailure:
            {
                var body = JsonSerializer.Serialize(new
                {
                    throw_transient = true,
                    type = "text",
                    text = "Transient failure test payload."
                });

                var signedReq = SimulatorChannelProvider.CreateSignedWebhookRequest(
                    rawBody: body,
                    secret: connection.WebhookVerificationToken);

                var ingressCmd = new WebhookIngressCommand(
                    Channel: ChannelType.Simulator,
                    ConnectionId: connection.Id,
                    Method: signedReq.Method,
                    Path: signedReq.Path,
                    Headers: signedReq.Headers,
                    QueryParameters: signedReq.QueryParameters,
                    RawBody: signedReq.RawBody,
                    ContentType: "application/json",
                    CorrelationId: correlationId,
                    ReceivedAt: now);

                var res = await ingressService.HandleWebhookAsync(ingressCmd, cancellationToken);
                generatedId = res.EventId;
                if (res.IsSuccess && generatedId is not null)
                {
                    await processingService.ProcessWebhookEventAsync(generatedId, cancellationToken);
                }
                summary = $"Webhook event {generatedId} simulated transient timeout; transitioned to Failed with retry interval.";
                break;
            }

            case SimulatorScenarioType.PermanentPoison:
            {
                var body = JsonSerializer.Serialize(new
                {
                    is_poison = true,
                    type = "text",
                    text = "Poison malformed payload."
                });

                var signedReq = SimulatorChannelProvider.CreateSignedWebhookRequest(
                    rawBody: body,
                    secret: connection.WebhookVerificationToken);

                var ingressCmd = new WebhookIngressCommand(
                    Channel: ChannelType.Simulator,
                    ConnectionId: connection.Id,
                    Method: signedReq.Method,
                    Path: signedReq.Path,
                    Headers: signedReq.Headers,
                    QueryParameters: signedReq.QueryParameters,
                    RawBody: signedReq.RawBody,
                    ContentType: "application/json",
                    CorrelationId: correlationId,
                    ReceivedAt: now);

                var res = await ingressService.HandleWebhookAsync(ingressCmd, cancellationToken);
                generatedId = res.EventId;
                if (res.IsSuccess && generatedId is not null)
                {
                    await processingService.ProcessWebhookEventAsync(generatedId, cancellationToken);
                }
                summary = $"Poison webhook event {generatedId} immediately quarantined to DeadLetter (Permanent).";
                break;
            }

            case SimulatorScenarioType.TokenExpiry:
            {
                connection.UpdateHealth(
                    isHealthy: false,
                    status: ChannelConnectionStatus.Expired,
                    summary: "Token expired",
                    details: "Simulated token expiry scenario.",
                    checkedAt: now);
                await dbContext.SaveChangesAsync(cancellationToken);

                var health = await connectionService.CheckHealthAsync(connection.Id, cancellationToken);
                summary = $"Connection health check returned: IsHealthy={health.Value?.IsHealthy}, Status={health.Value?.Status}, Message={health.Value?.DiagnosticMessage}.";
                break;
            }

            case SimulatorScenarioType.Reconnect:
            {
                connection.UpdateHealth(
                    isHealthy: true,
                    status: ChannelConnectionStatus.Active,
                    summary: "Operational",
                    details: "Simulated reconnect scenario.",
                    checkedAt: now);
                connection.Enable();
                await dbContext.SaveChangesAsync(cancellationToken);

                var health = await connectionService.CheckHealthAsync(connection.Id, cancellationToken);
                summary = $"Connection reconnected and validated: IsHealthy={health.Value?.IsHealthy}, Status={health.Value?.Status}.";
                break;
            }

            case SimulatorScenarioType.DeliveryReceipt:
            {
                var body = JsonSerializer.Serialize(new
                {
                    type = "status",
                    message_id = "msg_receipt_target_" + Guid.NewGuid().ToString("N")[..8],
                    sender_id = "+9779800000001"
                });

                var signedReq = SimulatorChannelProvider.CreateSignedWebhookRequest(
                    rawBody: body,
                    secret: connection.WebhookVerificationToken);

                var ingressCmd = new WebhookIngressCommand(
                    Channel: ChannelType.Simulator,
                    ConnectionId: connection.Id,
                    Method: signedReq.Method,
                    Path: signedReq.Path,
                    Headers: signedReq.Headers,
                    QueryParameters: signedReq.QueryParameters,
                    RawBody: signedReq.RawBody,
                    ContentType: "application/json",
                    CorrelationId: correlationId,
                    ReceivedAt: now);

                var res = await ingressService.HandleWebhookAsync(ingressCmd, cancellationToken);
                generatedId = res.EventId;
                if (res.IsSuccess && generatedId is not null)
                {
                    await processingService.ProcessWebhookEventAsync(generatedId, cancellationToken);
                }
                summary = $"Delivery receipt event {generatedId} processed successfully.";
                break;
            }
        }

        await auditEvents.AppendAsync(new AuditEventWrite(
            Action: "integrations.simulator.scenario_executed",
            TargetType: "channel-connection",
            TargetId: connection.Id,
            Metadata: JsonSerializer.Serialize(new
            {
                scenario = request.Scenario.ToString(),
                generatedId,
                correlationId,
                summary
            })), cancellationToken);

        return Result<SimulatorScenarioResult>.Success(new SimulatorScenarioResult(
            Scenario: request.Scenario,
            Succeeded: true,
            Summary: summary,
            GeneratedEventId: generatedId,
            CorrelationId: correlationId,
            ExecutedAt: DateTimeOffset.UtcNow));
    }
}
