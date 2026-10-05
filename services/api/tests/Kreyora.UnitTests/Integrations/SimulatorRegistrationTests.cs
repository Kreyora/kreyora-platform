using Kreyora.Application.Integrations;
using Kreyora.Infrastructure;
using Kreyora.Infrastructure.Integrations.Simulator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;

namespace Kreyora.UnitTests.Integrations;

/// <summary>The composed Simulator provider must not accept the fixed test signature outside Development/Testing.</summary>
public sealed class SimulatorRegistrationTests
{
    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    public async Task RegisteredSimulator_AcceptsTheFixedSignatureOnlyInDevelopmentAndTesting(string environmentName, bool accepted)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().Build(), new HostingEnvironment { EnvironmentName = environmentName });
        var simulator = services
            .Where(d => d.ServiceType == typeof(IChannelProvider))
            .Select(d => d.ImplementationInstance)
            .OfType<SimulatorChannelProvider>()
            .Single();

        var request = WebhookValidationRequest.Create(
            rawBody: "{\"id\":\"evt_forged\",\"text\":\"forged\"}",
            headers: new Dictionary<string, string> { ["X-Hub-Signature-256"] = SimulatorChannelProvider.DefaultValidSignature });

        Assert.Equal(accepted, (await simulator.ValidateWebhookAsync(request)).IsValid);
    }
}
