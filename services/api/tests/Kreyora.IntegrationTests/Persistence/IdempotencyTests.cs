using Kreyora.Infrastructure.Persistence.Entities;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Persistence;

public class IdempotencyTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public IdempotencyTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UniqueKey_Prevents_DuplicateInsert()
    {
        using var context = _fixture.CreateDbContext();
        await context.Database.MigrateAsync();

        var key = $"idempotency-test-{Guid.NewGuid():N}";

        context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            IdempotencyKey = key,
            ConsumerName = "TestConsumer"
        });
        await context.SaveChangesAsync();

        using var context2 = _fixture.CreateDbContext();
        context2.IdempotencyRecords.Add(new IdempotencyRecord
        {
            IdempotencyKey = key,
            ConsumerName = "TestConsumer"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context2.SaveChangesAsync());
    }
}
