using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Persistence;

public class TransactionTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public TransactionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Commit_Persists_Changes()
    {
        using var context = _fixture.CreateDbContext();
        await context.Database.MigrateAsync();

        var uow = new UnitOfWork(context);
        await uow.BeginTransactionAsync();

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = "TestEvent",
            Content = "{\"test\":true}"
        });
        await uow.SaveChangesAsync();
        await uow.CommitTransactionAsync();

        using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.OutboxMessages.CountAsync();
        Assert.True(count > 0);
    }

    [Fact]
    public async Task Rollback_Discards_Changes()
    {
        using var context = _fixture.CreateDbContext();
        await context.Database.MigrateAsync();

        var countBefore = await context.OutboxMessages.CountAsync();

        var uow = new UnitOfWork(context);
        await uow.BeginTransactionAsync();

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = "RollbackEvent",
            Content = "{\"rollback\":true}"
        });
        await uow.SaveChangesAsync();
        await uow.RollbackTransactionAsync();

        using var verifyContext = _fixture.CreateDbContext();
        var countAfter = await verifyContext.OutboxMessages.CountAsync();
        Assert.Equal(countBefore, countAfter);
    }
}
