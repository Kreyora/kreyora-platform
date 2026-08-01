using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kreyora.WebApi.Seeding;

public static partial class DevSeedHook
{
    public static async Task SeedDevelopmentDataAsync(IServiceProvider services)
    {
        var env = services.GetRequiredService<IHostEnvironment>();
        if (!env.IsDevelopment())
        {
            throw new InvalidOperationException("Development seed data can only be created in the Development environment.");
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DevSeed");
        LogSeedStarted(logger);

        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var roleName in RoleDefinitions.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(roleName));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Unable to seed the {roleName} role: {string.Join("; ", result.Errors.Select(error => error.Description))}");
                }
            }
        }

        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var demoPassword = configuration["Development:Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(demoPassword))
        {
            LogDemoSeedSkipped(logger);
            LogSeedCompleted(logger);
            return;
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        const string demoEmail = "owner@kreyora.test";
        var demoUser = await userManager.FindByEmailAsync(demoEmail);
        if (demoUser is null)
        {
            demoUser = new ApplicationUser
            {
                DisplayName = "Development Owner",
                Email = demoEmail,
                UserName = demoEmail
            };

            var result = await userManager.CreateAsync(demoUser, demoPassword);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Unable to seed the development demo user: {string.Join("; ", result.Errors.Select(error => error.Description))}");
            }
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
            candidate => candidate.NormalizedSlug == "DEVELOPMENT-STORE");
        var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipService>();
        if (tenant is null)
        {
            tenant = await memberships.CreateTenantForOwnerAsync(
                new CreateTenantForOwnerRequest(demoUser.Id, "Development Store", "development-store"));
        }
        else if (!await dbContext.Memberships.AnyAsync(
                     membership => membership.TenantId == tenant.Id && membership.UserId == demoUser.Id))
        {
            await memberships.GrantMembershipAsync(tenant.Id, demoUser.Id, TenantRole.Owner);
        }

        LogSeedCompleted(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development seed hook started")]
    private static partial void LogSeedStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Development demo user was skipped because Development__Seed__DemoPassword is not configured")]
    private static partial void LogDemoSeedSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Development seed hook completed")]
    private static partial void LogSeedCompleted(ILogger logger);
}
