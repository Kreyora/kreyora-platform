using Kreyora.Application.Tenancy;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Persistence;

public class TransactionTests : IClassFixture<PostgresFixture>
{
    private const string TenantId = "01J00000000000000000000002";
    private readonly PostgresFixture _fixture;

    public TransactionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Commit_Persists_Changes()
    {
        var tenantContext = new TenantContextAccessor();
        using var scope = tenantContext.BeginScope(new TenantContext(TenantId, null, null, null));
        using var context = _fixture.CreateDbContext(tenantContext);
        await context.Database.MigrateAsync();

        var uow = new UnitOfWork(context);
        await uow.BeginTransactionAsync();

        context.OutboxMessages.Add(new OutboxMessage
        {
            TenantId = TenantId,
            Type = "TestEvent",
            Content = "{\"test\":true}"
        });
        await uow.SaveChangesAsync();
        await uow.CommitTransactionAsync();

        using var verifyContext = _fixture.CreateDbContext(tenantContext);
        var count = await verifyContext.OutboxMessages.CountAsync();
        Assert.True(count > 0);
    }

    [Fact]
    public async Task Rollback_Discards_Changes()
    {
        var tenantContext = new TenantContextAccessor();
        using var scope = tenantContext.BeginScope(new TenantContext(TenantId, null, null, null));
        using var context = _fixture.CreateDbContext(tenantContext);
        await context.Database.MigrateAsync();

        var countBefore = await context.OutboxMessages.CountAsync();

        var uow = new UnitOfWork(context);
        await uow.BeginTransactionAsync();

        context.OutboxMessages.Add(new OutboxMessage
        {
            TenantId = TenantId,
            Type = "RollbackEvent",
            Content = "{\"rollback\":true}"
        });
        await uow.SaveChangesAsync();
        await uow.RollbackTransactionAsync();

        using var verifyContext = _fixture.CreateDbContext(tenantContext);
        var countAfter = await verifyContext.OutboxMessages.CountAsync();
        Assert.Equal(countBefore, countAfter);
    }
}
