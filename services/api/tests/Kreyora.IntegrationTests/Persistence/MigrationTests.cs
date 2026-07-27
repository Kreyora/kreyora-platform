using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.IntegrationTests.Persistence;

public class MigrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public MigrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migrations_ApplyCleanly_ToEmptyDatabase()
    {
        using var context = _fixture.CreateDbContext();
        await context.Database.MigrateAsync();

        var applied = await context.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
    }

    [Fact]
    public async Task Migrations_AreIdempotent()
    {
        using var context = _fixture.CreateDbContext();
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync();

        var pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }
}
