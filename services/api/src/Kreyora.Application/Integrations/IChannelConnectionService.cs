using Kreyora.Application.Models;
using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public interface IChannelConnectionService
{
    Task<Result<ChannelConnectionDto>> CreateConnectionAsync(
        CreateChannelConnectionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ChannelConnectionDto>> UpdateConnectionAsync(
        string connectionId,
        UpdateChannelConnectionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ChannelConnectionDto>> RotateConnectionSecretsAsync(
        string connectionId,
        string targetKeyVersion,
        CancellationToken cancellationToken = default);

    Task<Result<ChannelConnectionDto>> DisableConnectionAsync(
        string connectionId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<Result<ChannelConnectionDto>> EnableConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> DeleteConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    Task<Result<ChannelConnectionDto>> GetConnectionByIdAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ChannelConnectionDto>>> GetConnectionsAsync(
        string? storeId = null,
        ChannelType? channel = null,
        CancellationToken cancellationToken = default);

    Task<Result<ConnectionHealthResult>> CheckHealthAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
}

