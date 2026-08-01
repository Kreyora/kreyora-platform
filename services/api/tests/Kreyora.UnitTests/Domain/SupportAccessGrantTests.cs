using Kreyora.Domain.Tenancy;

namespace Kreyora.UnitTests.Domain;

public class SupportAccessGrantTests
{
    [Fact]
    public void Create_RejectsExpiryBeyondEightHours()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() => SupportAccessGrant.Create("tenant", "support", "owner", "Investigate a seller ticket", now.AddHours(8).AddSeconds(1), now));
    }

    [Fact]
    public void Create_RequiresReason_AndGrantCanBeRevokedOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var grant = SupportAccessGrant.Create("tenant", "support", "owner", "Investigate a seller ticket", now.AddHours(1), now);
        grant.Revoke("owner", now.AddMinutes(2));
        Assert.False(grant.IsActiveAt(now.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => grant.Revoke("owner", now.AddMinutes(4)));
    }
}
