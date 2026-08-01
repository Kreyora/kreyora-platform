using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Kreyora.WebApi.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kreyora.IntegrationTests.Seeding;

public class DevSeedHookTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public DevSeedHookTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Seed_RefusesToRunOutsideDevelopment()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Production))
            .BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(() => DevSeedHook.SeedDevelopmentDataAsync(services));
    }

    [Fact]
    public async Task Seed_IsIdempotent_AndCreatesNoDemoUserWithoutPassword()
    {
        await using (var withoutPassword = CreateServices(password: null))
        {
            await MigrateAsync(withoutPassword);
            await DevSeedHook.SeedDevelopmentDataAsync(withoutPassword);

            using var scope = withoutPassword.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(RoleDefinitions.All.Count, await context.Roles.CountAsync());
            Assert.Empty(await context.Users.ToListAsync());
        }

        await using var withPassword = CreateServices(password: "DevelopmentSeed!1");
        await DevSeedHook.SeedDevelopmentDataAsync(withPassword);
        await DevSeedHook.SeedDevelopmentDataAsync(withPassword);

        using var seededScope = withPassword.CreateScope();
        var seededContext = seededScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(RoleDefinitions.All.Count, await seededContext.Roles.CountAsync());
        Assert.Single(await seededContext.Users.ToListAsync());
        Assert.Single(await seededContext.Tenants.ToListAsync());
        var membership = await seededContext.Memberships.SingleAsync();
        Assert.Equal(TenantRole.Owner, membership.Role);
        Assert.Equal(MembershipStatus.Active, membership.Status);
    }

    private ServiceProvider CreateServices(string? password)
    {
        var settings = new Dictionary<string, string?>();
        if (password is not null)
        {
            settings["Development:Seed:DemoPassword"] = password;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Development));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        services
            .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<ITenantMembershipService, TenantMembershipService>();
        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Kreyora.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
