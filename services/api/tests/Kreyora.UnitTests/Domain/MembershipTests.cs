using Kreyora.Domain.Tenancy;

namespace Kreyora.UnitTests.Domain;

public class MembershipTests
{
    [Fact]
    public void LifecycleTransitions_UpdateMembershipState()
    {
        var membership = Membership.Grant("tenant_01", "user_01", TenantRole.Operator);
        var occurredAt = DateTimeOffset.UtcNow;

        membership.Suspend(occurredAt);
        Assert.Equal(MembershipStatus.Suspended, membership.Status);
        Assert.Equal(occurredAt, membership.SuspendedAt);

        membership.Reactivate();
        Assert.True(membership.IsActive);
        Assert.Null(membership.SuspendedAt);

        membership.Revoke(occurredAt);
        Assert.Equal(MembershipStatus.Revoked, membership.Status);
        Assert.Equal(occurredAt, membership.RevokedAt);
    }

    [Fact]
    public void Grant_RejectsValuesOutsideTenantRoles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Membership.Grant("tenant_01", "user_01", (TenantRole)99));
    }
}
