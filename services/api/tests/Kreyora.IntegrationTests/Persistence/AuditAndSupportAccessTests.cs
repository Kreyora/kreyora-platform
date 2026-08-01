using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Support;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Audit;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Support;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Persistence;

public class AuditAndSupportAccessTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public AuditAndSupportAccessTests(PostgresFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task AuditEvents_AreTenantScopedCursorPaginated_AndAppendOnly()
    {
        var accessor = new TenantContextAccessor();
        await using var context = fixture.CreateDbContext(accessor);
        await context.Database.MigrateAsync();
        var tenant = Tenant.Create("Audit tenant", $"audit-{Guid.NewGuid():N}");
        var otherTenant = Tenant.Create("Other audit tenant", $"audit-other-{Guid.NewGuid():N}");
        context.Tenants.AddRange(tenant, otherTenant);
        await context.SaveChangesAsync();
        var owner = new TenantContext(tenant.Id, "01H00000000000000000000000", "membership", TenantRole.Owner);
        var other = new TenantContext(otherTenant.Id, "01H00000000000000000000001", "membership", TenantRole.Owner);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var events = new AuditEventService(context, accessor, new Correlation("audit-test"), authorizer);

        using (accessor.BeginScope(owner))
        {
            await events.AppendAsync(new AuditEventWrite("test.one", "test", "one"));
            await events.AppendAsync(new AuditEventWrite("test.two", "test", "two"));
            var first = await events.GetPageAsync(null, 1);
            Assert.Single(first.Items);
            Assert.NotNull(first.NextCursor);
            var second = await events.GetPageAsync(first.NextCursor, 1);
            Assert.Single(second.Items);
            Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
        }

        using (accessor.BeginScope(other))
        {
            await events.AppendAsync(new AuditEventWrite("test.other", "test", "other"));
        }

        using (accessor.BeginScope(owner))
        {
            var recorded = await context.AuditEvents.SingleAsync(item => item.TargetId == "one");
            context.Entry(recorded).State = EntityState.Modified;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();

            recorded = await context.AuditEvents.SingleAsync(item => item.TargetId == "one");
            context.Remove(recorded);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task SupportResolution_RequiresLiveRoleAndUnrevokedGrant()
    {
        var accessor = new TenantContextAccessor();
        await using var context = fixture.CreateDbContext(accessor);
        await context.Database.MigrateAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var tenant = Tenant.Create("Support tenant", $"support-{suffix}");
        var supportUser = CreateUser($"support-{suffix}@kreyora.test");
        var role = await GetOrCreatePlatformSupportRoleAsync(context);
        context.Tenants.Add(tenant);
        context.Users.Add(supportUser);
        await context.SaveChangesAsync();
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = supportUser.Id, RoleId = role.Id });
        await context.SaveChangesAsync();

        SupportAccessGrant grant;
        using (accessor.BeginScope(new TenantContext(tenant.Id, "01H00000000000000000000002", "membership", TenantRole.Owner)))
        {
            grant = SupportAccessGrant.Create(tenant.Id, supportUser.Id, "01H00000000000000000000002", "Investigate a seller request", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);
            context.SupportAccessGrants.Add(grant);
            await context.SaveChangesAsync();
        }

        var resolver = new TenantContextResolutionService(context);
        var resolved = await resolver.ResolveSupportContextAsync(supportUser.Id, tenant.Id);
        Assert.NotNull(resolved);
        Assert.True(resolved!.IsReadOnlySupport);
        Assert.Equal(grant.Id, resolved.SupportAccessGrantId);

        using (accessor.BeginScope(new TenantContext(tenant.Id, "01H00000000000000000000002", "membership", TenantRole.Owner)))
        {
            grant.Revoke("01H00000000000000000000002", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        Assert.Null(await resolver.ResolveSupportContextAsync(supportUser.Id, tenant.Id));
    }

    [Fact]
    public async Task SupportGrantService_RejectsOverlappingGrantAndWritesAudit()
    {
        var accessor = new TenantContextAccessor();
        await using var context = fixture.CreateDbContext(accessor);
        await context.Database.MigrateAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var tenant = Tenant.Create("Grant tenant", $"grant-{suffix}");
        var supportUser = CreateUser($"grant-support-{suffix}@kreyora.test");
        var role = await GetOrCreatePlatformSupportRoleAsync(context);
        context.Tenants.Add(tenant);
        context.Users.Add(supportUser);
        await context.SaveChangesAsync();
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = supportUser.Id, RoleId = role.Id });
        await context.SaveChangesAsync();
        var owner = new TenantContext(tenant.Id, "01H00000000000000000000003", "membership", TenantRole.Owner);
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(context, accessor, new Correlation("grant-test"), authorizer);
        var service = new SupportAccessGrantService(context, accessor, authorizer, audit);

        using (accessor.BeginScope(owner))
        {
            var created = await service.CreateAsync(new CreateSupportAccessGrantRequest(supportUser.Id, "Investigate an approved support issue", DateTimeOffset.UtcNow.AddHours(1)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new CreateSupportAccessGrantRequest(supportUser.Id, "Duplicate", DateTimeOffset.UtcNow.AddHours(1))));
            Assert.Equal(created.Id, (await context.AuditEvents.SingleAsync(item => item.Action == "support-access.granted")).TargetId);
        }
    }

    private static ApplicationUser CreateUser(string email) => new()
    {
        DisplayName = "Support Test User",
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant()
    };

    private static async Task<IdentityRole> GetOrCreatePlatformSupportRoleAsync(Kreyora.Infrastructure.Persistence.AppDbContext context)
    {
        var existing = await context.Roles.SingleOrDefaultAsync(candidate => candidate.Name == RoleDefinitions.PlatformSupport);
        if (existing is not null) return existing;
        var role = new IdentityRole(RoleDefinitions.PlatformSupport) { NormalizedName = RoleDefinitions.PlatformSupport.ToUpperInvariant() };
        context.Roles.Add(role);
        await context.SaveChangesAsync();
        return role;
    }

    private sealed class Correlation(string value) : ICorrelationContext
    {
        public string CorrelationId => value;
        public void SetCorrelationId(string correlationId) { }
    }
}
