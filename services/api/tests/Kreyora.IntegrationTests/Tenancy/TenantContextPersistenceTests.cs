using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Persistence.Entities;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Tenancy;

public class TenantContextPersistenceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public TenantContextPersistenceTests(PostgresFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Resolver_OnlyReturnsActiveMembershipsInActiveTenants()
    {
        await using var context = fixture.CreateDbContext();
        await context.Database.MigrateAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var user = CreateUser($"context-{suffix}@kreyora.test");
        var activeTenant = Tenant.Create("Active workspace", $"active-{suffix}");
        var suspendedTenant = Tenant.Create("Suspended workspace", $"suspended-{suffix}");
        suspendedTenant.SetStatus(TenantStatus.Suspended);
        var revokedTenant = Tenant.Create("Revoked workspace", $"revoked-{suffix}");

        context.Users.Add(user);
        context.Tenants.AddRange(activeTenant, suspendedTenant, revokedTenant);
        context.Memberships.AddRange(
            Membership.Grant(activeTenant.Id, user.Id, TenantRole.Owner),
            Membership.Grant(suspendedTenant.Id, user.Id, TenantRole.Admin),
            Membership.Grant(revokedTenant.Id, user.Id, TenantRole.Viewer));
        await context.SaveChangesAsync();

        var revokedMembership = await context.Memberships.SingleAsync(membership => membership.TenantId == revokedTenant.Id);
        revokedMembership.Revoke(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        var resolver = new TenantContextResolutionService(context);
        var workspaces = await resolver.GetActiveWorkspacesAsync(user.Id);
        var active = await resolver.ResolveMembershipContextAsync(user.Id, activeTenant.Id);
        var suspended = await resolver.ResolveMembershipContextAsync(user.Id, suspendedTenant.Id);
        var revoked = await resolver.ResolveMembershipContextAsync(user.Id, revokedTenant.Id);
        var forged = await resolver.ResolveMembershipContextAsync(user.Id, Tenant.Create("Foreign", $"foreign-{suffix}").Id);

        Assert.Single(workspaces);
        Assert.Equal(activeTenant.Id, workspaces[0].TenantId);
        Assert.NotNull(active);
        Assert.Equal(user.Id, active!.UserId);
        Assert.Equal(TenantRole.Owner, active.Role);
        Assert.Null(suspended);
        Assert.Null(revoked);
        Assert.Null(forged);
    }

    [Fact]
    public async Task TenantQueryFilterAndService_KeepOutboxReadsAndWritesInsideCurrentTenant()
    {
        var accessor = new TenantContextAccessor();
        await using var context = fixture.CreateDbContext(accessor);
        await context.Database.MigrateAsync();
        var firstTenantId = Kreyora.Domain.Abstractions.IdGenerator.NewId();
        var secondTenantId = Kreyora.Domain.Abstractions.IdGenerator.NewId();

        using (accessor.BeginScope(new TenantContext(firstTenantId, null, null, null)))
        {
            context.OutboxMessages.Add(new OutboxMessage { TenantId = firstTenantId, Type = "First", Content = "{}" });
            await context.SaveChangesAsync();
        }

        using (accessor.BeginScope(new TenantContext(secondTenantId, null, null, null)))
        {
            context.OutboxMessages.Add(new OutboxMessage { TenantId = secondTenantId, Type = "Second", Content = "{}" });
            await context.SaveChangesAsync();
        }

        using (accessor.BeginScope(new TenantContext(firstTenantId, null, null, null)))
        {
            var scoped = await new TenantQueryService(accessor).ForCurrentTenant(context.OutboxMessages).ToListAsync();
            var rawProjection = await context.OutboxMessages
                .FromSqlInterpolated($"SELECT * FROM outbox_messages WHERE tenant_id = {secondTenantId}")
                .Select(message => new { message.TenantId, message.Type })
                .ToListAsync();

            Assert.All(scoped, message => Assert.Equal(firstTenantId, message.TenantId));
            Assert.Empty(rawProjection);
        }

        using (accessor.BeginScope(new TenantContext(firstTenantId, null, null, null)))
        {
            context.OutboxMessages.Add(new OutboxMessage { TenantId = secondTenantId, Type = "Forged", Content = "{}" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task JobAndOutboxProcessors_EstablishTenantContextAndClearItAfterFailure()
    {
        var accessor = new TenantContextAccessor();
        await using var context = fixture.CreateDbContext(accessor);
        await context.Database.MigrateAsync();
        var tenant = Tenant.Create("Job workspace", $"jobs-{Guid.NewGuid():N}");
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var resolver = new TenantContextResolutionService(context);
        var runner = new TenantJobRunner(accessor, resolver);
        var processor = new TenantOutboxProcessor(accessor, resolver);
        TenantContext? observedJobContext = null;
        TenantContext? observedOutboxContext = null;

        await runner.RunAsync(new TenantJobEnvelope(tenant.Id, "test", "{}"), _ =>
        {
            observedJobContext = accessor.Current;
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(tenant.Id, _ =>
        {
            observedOutboxContext = accessor.Current;
            throw new InvalidOperationException("expected test failure");
        }));

        Assert.Equal(tenant.Id, observedJobContext!.TenantId);
        Assert.Equal(tenant.Id, observedOutboxContext!.TenantId);
        Assert.Null(accessor.Current);
    }

    private static ApplicationUser CreateUser(string email) => new()
    {
        DisplayName = "Tenant Context Tester",
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant()
    };
}
