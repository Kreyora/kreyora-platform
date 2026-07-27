using Kreyora.Infrastructure.Persistence.Entities;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Persistence;

public class OutboxTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public OutboxTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OutboxMessage_CanBePersisted_AndRetrieved()
    {
        using var context = _fixture.CreateDbContext();
        await context.Database.MigrateAsync();

        var message = new OutboxMessage
        {
            Type = "OrderCreated",
            Content = "{\"orderId\":\"123\"}"
        };

        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        using var verifyContext = _fixture.CreateDbContext();
        var retrieved = await verifyContext.OutboxMessages.FindAsync(message.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("OrderCreated", retrieved!.Type);
        Assert.Null(retrieved.ProcessedAt);
    }

    [Fact]
    public async Task UnprocessedMessages_CanBeQueried()
    {
        using var context = _fixture.CreateDbContext();
        await context.Database.MigrateAsync();

        var unprocessed = new OutboxMessage
        {
            Type = "Unprocessed",
            Content = "{}"
        };
        var processed = new OutboxMessage
        {
            Type = "Processed",
            Content = "{}",
            ProcessedAt = DateTimeOffset.UtcNow
        };

        context.OutboxMessages.AddRange(unprocessed, processed);
        await context.SaveChangesAsync();

        using var verifyContext = _fixture.CreateDbContext();
        var pending = await verifyContext.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .ToListAsync();

        Assert.Contains(pending, m => m.Id == unprocessed.Id);
        Assert.DoesNotContain(pending, m => m.Id == processed.Id);
    }
}
