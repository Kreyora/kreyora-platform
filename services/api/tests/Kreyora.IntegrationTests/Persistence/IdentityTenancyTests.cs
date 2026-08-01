using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Persistence;

public class IdentityTenancyTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public IdentityTenancyTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task IdentityAndTenancyMigration_CreatesRequiredTables()
    {
        await using var context = _fixture.CreateDbContext();
        await context.Database.MigrateAsync();

        var migrations = await context.Database.GetAppliedMigrationsAsync();

        Assert.Contains(migrations, migration => migration.EndsWith("AddIdentityTenancyAndMemberships", StringComparison.Ordinal));
        Assert.Contains(migrations, migration => migration.EndsWith("AddTenantContextToOutbox", StringComparison.Ordinal));
        Assert.Contains(migrations, migration => migration.EndsWith("AddPolicyRbacAndAuditEvents", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NormalizedTenantSlug_AndIdentityFields_AreUnique()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"identity-{suffix}@kreyora.test";

        await using (var context = _fixture.CreateDbContext())
        {
            await context.Database.MigrateAsync();
            context.Tenants.Add(Tenant.Create("Unique Tenant", $"unique-{suffix}"));
            context.Users.Add(CreateUser(email));
            await context.SaveChangesAsync();
        }

        await using (var duplicateContext = _fixture.CreateDbContext())
        {
            duplicateContext.Tenants.Add(Tenant.Create("Duplicate Tenant", $"UNIQUE-{suffix}"));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        await using var duplicateUserContext = _fixture.CreateDbContext();
        duplicateUserContext.Users.Add(CreateUser(email.ToUpperInvariant()));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateUserContext.SaveChangesAsync());
    }

    [Fact]
    public async Task MembershipUniqueness_AndOwnerLifecycleInvariant_AreEnforced()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var context = _fixture.CreateDbContext();
        await context.Database.MigrateAsync();

        var firstUser = CreateUser($"owner-one-{suffix}@kreyora.test");
        var secondUser = CreateUser($"owner-two-{suffix}@kreyora.test");
        context.Users.AddRange(firstUser, secondUser);
        await context.SaveChangesAsync();

        var service = new TenantMembershipService(context);
        var tenant = await service.CreateTenantForOwnerAsync(
            new CreateTenantForOwnerRequest(firstUser.Id, "Owner Invariant Tenant", $"owners-{suffix}"));
        var firstOwner = await context.Memberships.SingleAsync(membership => membership.TenantId == tenant.Id && membership.UserId == firstUser.Id);

        await using (var duplicateMembershipContext = _fixture.CreateDbContext())
        {
            duplicateMembershipContext.Memberships.Add(Membership.Grant(tenant.Id, firstUser.Id, TenantRole.Owner));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateMembershipContext.SaveChangesAsync());
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SuspendMembershipAsync(firstOwner.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevokeMembershipAsync(firstOwner.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeMembershipRoleAsync(firstOwner.Id, TenantRole.Admin));

        var secondOwner = await service.GrantMembershipAsync(tenant.Id, secondUser.Id, TenantRole.Owner);
        await service.ChangeMembershipRoleAsync(firstOwner.Id, TenantRole.Admin);

        var reloadedFirstOwner = await context.Memberships.SingleAsync(membership => membership.Id == firstOwner.Id);
        var reloadedSecondOwner = await context.Memberships.SingleAsync(membership => membership.Id == secondOwner.Id);
        Assert.Equal(TenantRole.Admin, reloadedFirstOwner.Role);
        Assert.Equal(TenantRole.Owner, reloadedSecondOwner.Role);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GrantMembershipAsync(tenant.Id, secondUser.Id, TenantRole.Owner));
    }

    private static ApplicationUser CreateUser(string email) => new()
    {
        DisplayName = "Test User",
        Email = email.ToLowerInvariant(),
        NormalizedEmail = email.ToUpperInvariant(),
        UserName = email.ToLowerInvariant(),
        NormalizedUserName = email.ToUpperInvariant()
    };
}
