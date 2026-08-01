using System.Text.Json;

namespace Kreyora.Application.Audit;

public static class AuditMetadataSanitizer
{
    private static readonly string[] SensitiveNames = ["password", "secret", "token", "authorization", "cookie", "apikey", "api_key"];

    public static string? Sanitize(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;
        using var document = JsonDocument.Parse(metadata);
        return JsonSerializer.Serialize(SanitizeElement(document.RootElement));
    }

    private static object? SanitizeElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => IsSensitive(property.Name) ? "[REDACTED]" : SanitizeElement(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(SanitizeElement).ToArray(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var value) ? value : element.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    private static bool IsSensitive(string name) => SensitiveNames.Any(sensitive => name.Contains(sensitive, StringComparison.OrdinalIgnoreCase));
}
