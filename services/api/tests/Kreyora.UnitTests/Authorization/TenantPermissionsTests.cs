using Kreyora.Application.Authorization;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;

namespace Kreyora.UnitTests.Authorization;

public class TenantPermissionsTests
{
    [Theory]
    [InlineData(TenantRole.Owner, TenantPermissions.BillingManage, true)]
    [InlineData(TenantRole.Admin, TenantPermissions.BillingManage, false)]
    [InlineData(TenantRole.Admin, TenantPermissions.CatalogWrite, true)]
    [InlineData(TenantRole.Operator, TenantPermissions.PaymentsManage, false)]
    [InlineData(TenantRole.Operator, TenantPermissions.PaymentsRead, true)]
    [InlineData(TenantRole.Viewer, TenantPermissions.OrdersWrite, false)]
    [InlineData(TenantRole.Viewer, TenantPermissions.ReportingRead, true)]
    [InlineData(TenantRole.Viewer, TenantPermissions.CatalogRead, true)]
    [InlineData(TenantRole.Viewer, TenantPermissions.IntegrationsRead, true)]
    [InlineData(TenantRole.Viewer, TenantPermissions.AiConfigurationRead, true)]
    [InlineData(TenantRole.Viewer, TenantPermissions.CatalogWrite, false)]
    public void Matrix_EnforcesTheApprovedRoleDecision(TenantRole role, string permission, bool expected)
    {
        var context = new TenantContext("01H00000000000000000000000", "user", "membership", role);
        Assert.Equal(expected, TenantPermissions.IsAllowed(context, permission));
    }

    [Fact]
    public void Admin_CannotManageAnOwnerMembership()
    {
        var admin = new TenantContext("01H00000000000000000000000", "user", "membership", TenantRole.Admin);
        Assert.False(TenantPermissions.CanManageMembership(admin, TenantRole.Owner));
        Assert.True(TenantPermissions.CanManageMembership(admin, TenantRole.Operator));
    }

    [Fact]
    public void ReadOnlySupport_HasOnlyAuditAndAdvisoryPermissions()
    {
        var support = new TenantContext("01H00000000000000000000000", "support", null, null, "grant");
        Assert.True(TenantPermissions.IsAllowed(support, TenantPermissions.AuditRead));
        Assert.False(TenantPermissions.IsAllowed(support, TenantPermissions.CatalogWrite));
    }
}
