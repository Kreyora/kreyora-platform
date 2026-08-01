using Kreyora.Domain.Tenancy;

namespace Kreyora.UnitTests.Domain;

public class RoleDefinitionsTests
{
    [Theory]
    [InlineData(RoleDefinitions.Owner)]
    [InlineData(RoleDefinitions.Admin)]
    [InlineData(RoleDefinitions.Operator)]
    [InlineData(RoleDefinitions.Viewer)]
    public void TenantRoles_AreRecognized(string role)
    {
        Assert.True(RoleDefinitions.IsTenantRole(role));
    }

    [Fact]
    public void PlatformSupport_IsNotATenantMembershipRole()
    {
        Assert.Contains(RoleDefinitions.PlatformSupport, RoleDefinitions.All);
        Assert.False(RoleDefinitions.IsTenantRole(RoleDefinitions.PlatformSupport));
    }
}
