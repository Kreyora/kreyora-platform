using Kreyora.Application.Integrations;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Conversations;
using Kreyora.Domain.Customers;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Kreyora.WebApi.Seeding;

/// <summary>
/// Development-only end-to-end fixtures (M08-S06), enabled with <c>Development:Seed:E2ePersonas=true</c>:
/// Admin/Operator/Viewer personas sharing the run's demo password, a Simulator connection signed with the
/// run-scoped <c>Development:Seed:SimulatorSecret</c>, and conversations inside and outside the reply window.
/// No secrets are committed; the harness generates them per run.
/// </summary>
internal static class E2eSeed
{
    public const string SimulatorAccountId = "e2e-simulator";

    public static readonly (string Email, string Name, TenantRole Role)[] Personas =
    [
        ("admin@kreyora.test", "E2E Admin", TenantRole.Admin),
        ("operator@kreyora.test", "E2E Operator", TenantRole.Operator),
        ("viewer@kreyora.test", "E2E Viewer", TenantRole.Viewer),
    ];

    public static async Task SeedAsync(IServiceProvider services, Tenant tenant, ApplicationUser owner, string password, IConfiguration configuration)
    {
        var simulatorSecret = configuration["Development:Seed:SimulatorSecret"];
        if (string.IsNullOrWhiteSpace(simulatorSecret))
        {
            throw new InvalidOperationException("Development:Seed:SimulatorSecret is required when E2E personas are enabled.");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var memberships = services.GetRequiredService<ITenantMembershipService>();
        var dbContext = services.GetRequiredService<AppDbContext>();

        foreach (var (email, name, role) in Personas)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser { DisplayName = name, Email = email, UserName = email };
                var created = await userManager.CreateAsync(user, password);
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException($"Unable to seed {email}: {string.Join("; ", created.Errors.Select(e => e.Description))}");
                }
            }

            if (!await dbContext.Memberships.AnyAsync(m => m.TenantId == tenant.Id && m.UserId == user.Id))
            {
                await memberships.GrantMembershipAsync(tenant.Id, user.Id, role);
            }
        }

        if (await dbContext.ChannelConnections.IgnoreQueryFilters().AnyAsync(c => c.Channel == ChannelType.Simulator && c.ExternalAccountId == SimulatorAccountId))
        {
            return;
        }

        var accessor = services.GetRequiredService<ITenantContextAccessor>();
        var encryption = services.GetRequiredService<ISecretEncryptionService>();
        using var scope = accessor.BeginScope(new TenantContext(tenant.Id, owner.Id, null, TenantRole.Owner));

        var connection = ChannelConnection.Create(tenant.Id, ChannelType.Simulator, SimulatorAccountId, "E2E Simulator",
            encryptedCredentials: encryption.Encrypt(simulatorSecret));
        dbContext.ChannelConnections.Add(connection);

        var now = DateTimeOffset.UtcNow;
        AddConversation(dbContext, connection, "e2e-customer-open", "Do you have the red kurta in size M?", now.AddMinutes(-5));
        AddConversation(dbContext, connection, "e2e-customer-second", "Is delivery available in Pokhara?", now.AddMinutes(-2));
        AddConversation(dbContext, connection, "e2e-customer-stale", "Hello? Anyone there?", now.AddDays(-8));
        await dbContext.SaveChangesAsync();
    }

    private static void AddConversation(AppDbContext dbContext, ChannelConnection connection, string customerId, string text, DateTimeOffset at)
    {
        var identity = CustomerChannelIdentity.Create(connection.TenantId, connection.Id, connection.Channel, customerId, at);
        var conversation = Conversation.Start(connection.TenantId, connection.Id, connection.StoreId, identity.Id, connection.Channel);
        conversation.RecordInboundMessage(at);
        dbContext.CustomerChannelIdentities.Add(identity);
        dbContext.Conversations.Add(conversation);
        dbContext.Messages.Add(Message.CreateInboundText(connection.TenantId, conversation.Id, connection.Id, null, $"e2e_{customerId}_1", text, at, at));
    }
}
