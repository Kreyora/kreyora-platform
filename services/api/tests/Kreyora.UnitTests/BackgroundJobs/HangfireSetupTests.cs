using Kreyora.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kreyora.UnitTests.BackgroundJobs;

public sealed class HangfireSetupTests
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    [Theory]
    [InlineData(null, 1)]
    [InlineData("true", 1)]
    [InlineData("false", 0)]
    public void ServerIsRegisteredUnlessDisabled(string? serverEnabled, int expectedServers)
    {
        var settings = new Dictionary<string, string?> { ["Database:ConnectionString"] = ConnectionString };
        if (serverEnabled is not null)
        {
            settings["BackgroundJobs:ServerEnabled"] = serverEnabled;
        }

        var services = new ServiceCollection();
        services.AddHangfireServices(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        Assert.Equal(expectedServers, services.Count(d => d.ServiceType == typeof(IHostedService)));
        // Enqueueing stays available either way: the storage-backed client is registered.
        Assert.Contains(services, d => d.ServiceType == typeof(Hangfire.IBackgroundJobClient));
    }
}
