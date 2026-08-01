using Kreyora.Application.Tenancy;
using Kreyora.Infrastructure.Persistence.Entities;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Persistence;

public class OutboxTests : IClassFixture<PostgresFixture>
{
    private const string TenantId = "01J00000000000000000000001";
    private readonly PostgresFixture _fixture;

    public OutboxTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OutboxMessage_CanBePersisted_AndRetrieved()
    {
        var tenantContext = CreateTenantContext();
        using var scope = tenantContext.BeginScope(new TenantContext(TenantId, null, null, null));
        using var context = _fixture.CreateDbContext(tenantContext);
        await context.Database.MigrateAsync();

        var message = new OutboxMessage
        {
            TenantId = TenantId,
            Type = "OrderCreated",
            Content = "{\"orderId\":\"123\"}"
        };

        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        using var verifyContext = _fixture.CreateDbContext(tenantContext);
        var retrieved = await verifyContext.OutboxMessages.FindAsync(message.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("OrderCreated", retrieved!.Type);
        Assert.Null(retrieved.ProcessedAt);
    }

    [Fact]
    public async Task UnprocessedMessages_CanBeQueried()
    {
        var tenantContext = CreateTenantContext();
        using var scope = tenantContext.BeginScope(new TenantContext(TenantId, null, null, null));
        using var context = _fixture.CreateDbContext(tenantContext);
        await context.Database.MigrateAsync();

        var unprocessed = new OutboxMessage
        {
            TenantId = TenantId,
            Type = "Unprocessed",
            Content = "{}"
        };
        var processed = new OutboxMessage
        {
            TenantId = TenantId,
            Type = "Processed",
            Content = "{}",
            ProcessedAt = DateTimeOffset.UtcNow
        };

        context.OutboxMessages.AddRange(unprocessed, processed);
        await context.SaveChangesAsync();

        using var verifyContext = _fixture.CreateDbContext(tenantContext);
        var pending = await verifyContext.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .ToListAsync();

        Assert.Contains(pending, m => m.Id == unprocessed.Id);
        Assert.DoesNotContain(pending, m => m.Id == processed.Id);
    }

    private static TenantContextAccessor CreateTenantContext() => new();
}
