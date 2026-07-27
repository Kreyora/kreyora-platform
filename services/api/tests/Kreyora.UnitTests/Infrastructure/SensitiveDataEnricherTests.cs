using Kreyora.Infrastructure.Logging;
using Serilog;
using Serilog.Events;

namespace Kreyora.UnitTests.Infrastructure;

public class SensitiveDataEnricherTests
{
    [Theory]
    [InlineData("Authorization")]
    [InlineData("Password")]
    [InlineData("Secret")]
    [InlineData("Token")]
    [InlineData("ConnectionString")]
    [InlineData("ApiKey")]
    [InlineData("Cookie")]
    public void Enricher_Redacts_SensitiveProperties(string propertyName)
    {
        LogEvent? capturedEvent = null;

        var logger = new LoggerConfiguration()
            .Enrich.With<SensitiveDataEnricher>()
            .WriteTo.Sink(new DelegateSink(e => capturedEvent = e))
            .CreateLogger();

        logger.ForContext(propertyName, "super-secret-value-123")
            .Information("Test log");

        Assert.NotNull(capturedEvent);
        Assert.True(capturedEvent!.Properties.ContainsKey(propertyName));

        var value = capturedEvent.Properties[propertyName].ToString();
        Assert.Equal("\"[REDACTED]\"", value);
    }

    [Fact]
    public void Enricher_DoesNotRedact_NonSensitiveProperties()
    {
        LogEvent? capturedEvent = null;

        var logger = new LoggerConfiguration()
            .Enrich.With<SensitiveDataEnricher>()
            .WriteTo.Sink(new DelegateSink(e => capturedEvent = e))
            .CreateLogger();

        logger.ForContext("UserName", "john")
            .Information("Test log");

        Assert.NotNull(capturedEvent);
        var value = capturedEvent!.Properties["UserName"].ToString();
        Assert.Equal("\"john\"", value);
    }

    private sealed class DelegateSink : Serilog.Core.ILogEventSink
    {
        private readonly Action<LogEvent> _action;
        public DelegateSink(Action<LogEvent> action) => _action = action;
        public void Emit(LogEvent logEvent) => _action(logEvent);
    }
}
