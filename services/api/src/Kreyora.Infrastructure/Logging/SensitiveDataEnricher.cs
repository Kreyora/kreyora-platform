using Serilog.Core;
using Serilog.Events;

namespace Kreyora.Infrastructure.Logging;

/// <summary>
/// Serilog enricher that redacts known sensitive property names.
/// Applied globally so any log call automatically masks secrets.
/// </summary>
public sealed class SensitiveDataEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> SensitiveProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "Password",
        "Secret",
        "Token",
        "ConnectionString",
        "ApiKey"
    };

    private const string Redacted = "[REDACTED]";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var keysToRedact = new List<string>();

        foreach (var prop in logEvent.Properties)
        {
            if (SensitiveProperties.Contains(prop.Key))
            {
                keysToRedact.Add(prop.Key);
            }
        }

        foreach (var key in keysToRedact)
        {
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(key, Redacted));
        }
    }
}
