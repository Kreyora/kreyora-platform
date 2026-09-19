using System.Text.Json;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Integrations;

public sealed class ChannelConnectionService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ITenantPermissionAuthorizer permissionAuthorizer,
    ISecretEncryptionService encryptionService,
    IAuditEventService auditEvents,
    IChannelProviderRegistry providerRegistry) : IChannelConnectionService
{
    public async Task<Result<ChannelConnectionDto>> CreateConnectionAsync(
        CreateChannelConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        var context = tenantContext.RequireCurrent();

        if (!string.IsNullOrWhiteSpace(request.StoreId))
        {
            var storeExists = await dbContext.Stores.AnyAsync(s => s.Id == request.StoreId, cancellationToken);
            if (!storeExists)
            {
                return Result<ChannelConnectionDto>.ValidationError("Referenced store was not found in this tenant.");
            }
        }

        var isDuplicate = await dbContext.ChannelConnections.AnyAsync(
            c => c.Channel == request.Channel && c.ExternalAccountId == request.ExternalAccountId,
            cancellationToken);

        if (isDuplicate)
        {
            return Result<ChannelConnectionDto>.Conflict(
                $"A connection for channel '{request.Channel}' with account ID '{request.ExternalAccountId}' already exists in this tenant.");
        }

        try
        {
            EncryptedSecret? encryptedCredentials = null;
            if (!string.IsNullOrWhiteSpace(request.PlainTextSecret))
            {
                encryptedCredentials = encryptionService.Encrypt(request.PlainTextSecret);
            }

            var connection = ChannelConnection.Create(
                tenantId: context.TenantId,
                channel: request.Channel,
                externalAccountId: request.ExternalAccountId,
                displayName: request.DisplayName,
                storeId: request.StoreId,
                encryptedCredentials: encryptedCredentials,
                webhookVerificationToken: request.WebhookVerificationToken,
                tokenExpiresAt: request.TokenExpiresAt,
                refreshTokenExpiresAt: request.RefreshTokenExpiresAt);

            dbContext.ChannelConnections.Add(connection);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEvents.AppendAsync(new AuditEventWrite(
                Action: "integrations.connection.created",
                TargetType: "channel-connection",
                TargetId: connection.Id,
                Metadata: JsonSerializer.Serialize(new
                {
                    channel = connection.Channel.ToString(),
                    externalAccountId = connection.ExternalAccountId,
                    displayName = connection.DisplayName,
                    storeId = connection.StoreId,
                    hasCredentials = connection.EncryptedCredentials != null,
                    keyVersion = connection.EncryptedCredentials?.KeyVersion
                })), cancellationToken);

            return Result<ChannelConnectionDto>.Success(Map(connection));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return Result<ChannelConnectionDto>.ValidationError(ex.Message);
        }
    }

    public async Task<Result<ChannelConnectionDto>> UpdateConnectionAsync(
        string connectionId,
        UpdateChannelConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        _ = tenantContext.RequireCurrent();

        var connection = await dbContext.ChannelConnections
            .SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection is null)
        {
            return Result<ChannelConnectionDto>.NotFound("Channel connection not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.StoreId) && request.StoreId != connection.StoreId)
        {
            var storeExists = await dbContext.Stores.AnyAsync(s => s.Id == request.StoreId, cancellationToken);
            if (!storeExists)
            {
                return Result<ChannelConnectionDto>.ValidationError("Referenced store was not found in this tenant.");
            }
        }

        try
        {
            if (request.DisplayName != null || request.StoreId != null)
            {
                connection.UpdateMetadata(
                    request.DisplayName ?? connection.DisplayName,
                    request.StoreId ?? connection.StoreId);
            }

            if (!string.IsNullOrWhiteSpace(request.PlainTextSecret))
            {
                var encrypted = encryptionService.Encrypt(request.PlainTextSecret);
                connection.UpdateCredentials(encrypted, request.TokenExpiresAt, request.RefreshTokenExpiresAt);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEvents.AppendAsync(new AuditEventWrite(
                Action: "integrations.connection.updated",
                TargetType: "channel-connection",
                TargetId: connection.Id,
                Metadata: JsonSerializer.Serialize(new
                {
                    displayName = connection.DisplayName,
                    storeId = connection.StoreId,
                    hasCredentials = connection.EncryptedCredentials != null,
                    keyVersion = connection.EncryptedCredentials?.KeyVersion
                })), cancellationToken);

            return Result<ChannelConnectionDto>.Success(Map(connection));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return Result<ChannelConnectionDto>.ValidationError(ex.Message);
        }
    }

    public async Task<Result<ChannelConnectionDto>> RotateConnectionSecretsAsync(
        string connectionId,
        string targetKeyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKeyVersion);
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        _ = tenantContext.RequireCurrent();

        var connection = await dbContext.ChannelConnections
            .SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection is null)
        {
            return Result<ChannelConnectionDto>.NotFound("Channel connection not found.");
        }

        if (connection.EncryptedCredentials is null)
        {
            return Result<ChannelConnectionDto>.ValidationError("Connection has no credentials to rotate.");
        }

        try
        {
            connection.RotateSecret((oldSecret, version) =>
            {
                var plainText = encryptionService.Decrypt(oldSecret);
                return encryptionService.Encrypt(plainText, version);
            }, targetKeyVersion);

            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEvents.AppendAsync(new AuditEventWrite(
                Action: "integrations.connection.secret_rotated",
                TargetType: "channel-connection",
                TargetId: connection.Id,
                Metadata: JsonSerializer.Serialize(new
                {
                    newKeyVersion = targetKeyVersion
                })), cancellationToken);

            return Result<ChannelConnectionDto>.Success(Map(connection));
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException or KeyNotFoundException)
        {
            return Result<ChannelConnectionDto>.ValidationError(ex.Message);
        }
    }

    public async Task<Result<ChannelConnectionDto>> DisableConnectionAsync(
        string connectionId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        _ = tenantContext.RequireCurrent();

        var connection = await dbContext.ChannelConnections
            .SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection is null)
        {
            return Result<ChannelConnectionDto>.NotFound("Channel connection not found.");
        }

        connection.Disable(reason);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEvents.AppendAsync(new AuditEventWrite(
            Action: "integrations.connection.disabled",
            TargetType: "channel-connection",
            TargetId: connection.Id,
            Metadata: JsonSerializer.Serialize(new { reason })), cancellationToken);

        return Result<ChannelConnectionDto>.Success(Map(connection));
    }

    public async Task<Result<ChannelConnectionDto>> EnableConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        _ = tenantContext.RequireCurrent();

        var connection = await dbContext.ChannelConnections
            .SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection is null)
        {
            return Result<ChannelConnectionDto>.NotFound("Channel connection not found.");
        }

        try
        {
            connection.Enable();
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEvents.AppendAsync(new AuditEventWrite(
                Action: "integrations.connection.enabled",
                TargetType: "channel-connection",
                TargetId: connection.Id), cancellationToken);

            return Result<ChannelConnectionDto>.Success(Map(connection));
        }
        catch (InvalidOperationException ex)
        {
            return Result<ChannelConnectionDto>.ValidationError(ex.Message);
        }
    }

    public async Task<Result<bool>> DeleteConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsWrite);
        _ = tenantContext.RequireCurrent();

        var connection = await dbContext.ChannelConnections
            .SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection is null)
        {
            return Result<bool>.NotFound("Channel connection not found.");
        }

        dbContext.ChannelConnections.Remove(connection);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEvents.AppendAsync(new AuditEventWrite(
            Action: "integrations.connection.deleted",
            TargetType: "channel-connection",
            TargetId: connection.Id,
            Metadata: JsonSerializer.Serialize(new
            {
                channel = connection.Channel.ToString(),
                externalAccountId = connection.ExternalAccountId
            })), cancellationToken);

        return Result<bool>.Success(true);
    }

    public async Task<Result<ChannelConnectionDto>> GetConnectionByIdAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        var connection = await dbContext.ChannelConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection is null)
        {
            return Result<ChannelConnectionDto>.NotFound("Channel connection not found.");
        }

        return Result<ChannelConnectionDto>.Success(Map(connection));
    }

    public async Task<Result<IReadOnlyList<ChannelConnectionDto>>> GetConnectionsAsync(
        string? storeId = null,
        ChannelType? channel = null,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        var query = dbContext.ChannelConnections.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(storeId))
        {
            query = query.Where(c => c.StoreId == storeId);
        }

        if (channel.HasValue)
        {
            query = query.Where(c => c.Channel == channel.Value);
        }

        var connections = await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ChannelConnectionDto>>.Success(
            connections.Select(Map).ToList());
    }

    public async Task<Result<ConnectionHealthResult>> CheckHealthAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        permissionAuthorizer.Demand(TenantPermissions.IntegrationsRead);
        _ = tenantContext.RequireCurrent();

        var connection = await dbContext.ChannelConnections
            .SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection is null)
        {
            return Result<ConnectionHealthResult>.NotFound("Channel connection not found.");
        }

        if (providerRegistry.TryGetProvider(connection.Channel, out var provider))
        {
            var snapshot = new ChannelConnectionSnapshot(
                ConnectionId: connection.Id,
                TenantId: connection.TenantId,
                StoreId: connection.StoreId,
                Channel: connection.Channel,
                ExternalAccountId: connection.ExternalAccountId,
                EncryptedCredentials: connection.EncryptedCredentials,
                Capabilities: connection.Capabilities,
                Status: connection.Status);

            var health = await provider.ValidateOrRefreshConnectionAsync(snapshot, cancellationToken);
            connection.UpdateHealth(
                isHealthy: health.IsHealthy,
                status: health.Status,
                summary: health.IsHealthy ? "Healthy" : "Degraded",
                details: health.DiagnosticMessage,
                checkedAt: health.CheckedAt);

            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<ConnectionHealthResult>.Success(health);
        }

        var defaultHealth = ConnectionHealthResult.Healthy("No active provider adapter registered; connection metadata valid.");
        return Result<ConnectionHealthResult>.Success(defaultHealth);
    }

    private static ChannelConnectionDto Map(ChannelConnection connection) => new(
        Id: connection.Id,
        TenantId: connection.TenantId,
        StoreId: connection.StoreId,
        Channel: connection.Channel,
        ExternalAccountId: connection.ExternalAccountId,
        DisplayName: connection.DisplayName,
        Status: connection.Status,
        HasCredentials: connection.EncryptedCredentials != null,
        KeyVersion: connection.EncryptedCredentials?.KeyVersion,
        TokenExpiresAt: connection.TokenExpiresAt,
        RefreshTokenExpiresAt: connection.RefreshTokenExpiresAt,
        LastRefreshedAt: connection.LastRefreshedAt,
        LastValidatedAt: connection.LastValidatedAt,
        LastHealthCheckAt: connection.LastHealthCheckAt,
        HealthSummary: connection.HealthSummary,
        HealthDetails: connection.HealthDetails,
        Capabilities: connection.Capabilities,
        CreatedAt: connection.CreatedAt,
        ModifiedAt: connection.ModifiedAt);
}

