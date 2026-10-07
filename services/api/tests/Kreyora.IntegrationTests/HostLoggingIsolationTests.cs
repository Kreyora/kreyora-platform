using Kreyora.IntegrationTests.Assistant;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kreyora.IntegrationTests;

/// <summary>
/// Each API host logs through its own Serilog pipeline (<c>preserveStaticLogger: true</c>). Without it, every new host
/// replaced the process-wide <c>Log.Logger</c>, so loggers created later in one host wrote into another host's sinks —
/// which made the M09-S06 log-redaction test fail in CI when test classes ran in parallel.
/// </summary>
public sealed class HostLoggingIsolationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task LoggersCreatedAfterAnotherHostStarts_StillWriteToTheirOwnHost()
    {
        var sinkA = new Sink();
        var sinkB = new Sink();
        await using var a = new AssistantTestHost(fixture.ConnectionString, new InMemoryStorage(), configureServices: s => s.AddSingleton<Serilog.Core.ILogEventSink>(sinkA));
        _ = a.Services;
        await using (var b = new AssistantTestHost(fixture.ConnectionString, new InMemoryStorage(), configureServices: s => s.AddSingleton<Serilog.Core.ILogEventSink>(sinkB)))
        {
            _ = b.Services; // a second host starts while A is running
            Write(a, "created-while-b-runs", "probe-1");
        }

        Write(a, "created-after-b-stopped", "probe-2");

        Assert.Equal(["probe-1", "probe-2"], sinkA.Lines.Where(l => l.StartsWith("probe", StringComparison.Ordinal)));
        Assert.DoesNotContain(sinkB.Lines, l => l.StartsWith("probe", StringComparison.Ordinal));
    }

    private static void Write(AssistantTestHost host, string category, string message) =>
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category).Log(LogLevel.Information, default, message, null, (state, _) => state);

    private sealed class Sink : Serilog.Core.ILogEventSink
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);
            Lines.Enqueue(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).Trim('"'));
        }
    }
}
