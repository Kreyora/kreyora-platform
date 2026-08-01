using Kreyora.Domain.Tenancy;

namespace Kreyora.Application.Tenancy;

/// <summary>
/// The verified tenant boundary for the current request or durable operation.
/// User, membership, and role are populated for seller requests and intentionally
/// absent for tenant-scoped system work such as outbox processing.
/// </summary>
public sealed record TenantContext(
    string TenantId,
    string? UserId,
    string? MembershipId,
    TenantRole? Role)
{
    public bool IsMembershipContext => UserId is not null && MembershipId is not null && Role is not null;
}

public interface ITenantContextAccessor
{
    TenantContext? Current { get; }

    TenantContext RequireCurrent();

    IDisposable BeginScope(TenantContext context);
}
