namespace Kreyora.Application.Support;

public interface ISupportAccessGrantService
{
    Task<SupportAccessGrantSummary> CreateAsync(CreateSupportAccessGrantRequest request, CancellationToken cancellationToken = default);
    Task RevokeAsync(string grantId, CancellationToken cancellationToken = default);
}

public sealed record CreateSupportAccessGrantRequest(string SupportUserId, string Reason, DateTimeOffset ExpiresAt);
public sealed record SupportAccessGrantSummary(string Id, string SupportUserId, DateTimeOffset ExpiresAt, string Reason, DateTimeOffset? RevokedAt);
