using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kreyora.Infrastructure.Assistant.Tools;

/// <summary>
/// Strict validator for the JSON-schema subset tool parameters use (M09-S04 Q2): <c>object</c> (properties, required;
/// unknown properties always rejected), <c>string</c> (minLength, maxLength, pattern, enum), <c>integer</c> (minimum,
/// maximum), <c>array</c> (items, minItems, maxItems) and <c>boolean</c>. Errors name fields, never echo values.
/// </summary>
public static class ToolSchemaValidator
{
    private static readonly HashSet<string> SupportedKeywords = new(StringComparer.Ordinal)
    {
        "type", "description", "properties", "required", "additionalProperties", "minLength", "maxLength", "pattern", "enum",
        "minimum", "maximum", "items", "minItems", "maxItems"
    };

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    public static IReadOnlyList<string> Validate(JsonElement schema, JsonElement value)
    {
        var errors = new List<string>();
        Check(schema, value, "$", errors);
        return errors;
    }

    /// <summary>Keywords in <paramref name="schema"/> this validator does not enforce (a registry test keeps this empty).</summary>
    public static IReadOnlyList<string> UnsupportedKeywords(JsonElement schema)
    {
        var found = new List<string>();
        Walk(schema, found);
        return found;

        static void Walk(JsonElement node, List<string> found)
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            foreach (var property in node.EnumerateObject())
            {
                if (!SupportedKeywords.Contains(property.Name)) found.Add(property.Name);
                if (property.Name == "properties")
                {
                    foreach (var child in property.Value.EnumerateObject()) Walk(child.Value, found);
                }
                else if (property.Name == "items")
                {
                    Walk(property.Value, found);
                }
                else if (property.Name == "additionalProperties" && property.Value.ValueKind != JsonValueKind.False)
                {
                    found.Add("additionalProperties:true");
                }
            }
        }
    }

    private static void Check(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        var type = schema.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "object":
                if (value.ValueKind != JsonValueKind.Object) { errors.Add($"{path}: must be an object"); return; }
                var properties = schema.TryGetProperty("properties", out var p) ? p : default;
                foreach (var property in value.EnumerateObject())
                {
                    if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(property.Name, out var child))
                    {
                        errors.Add($"{Join(path, property.Name)}: not allowed");
                        continue;
                    }

                    Check(child, property.Value, Join(path, property.Name), errors);
                }

                if (schema.TryGetProperty("required", out var required))
                {
                    foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
                    {
                        if (!value.TryGetProperty(name, out var present) || present.ValueKind == JsonValueKind.Null) errors.Add($"{Join(path, name)}: required");
                    }
                }

                break;

            case "string":
                if (value.ValueKind != JsonValueKind.String) { errors.Add($"{path}: must be a string"); return; }
                var text = value.GetString()!;
                if (schema.TryGetProperty("minLength", out var min) && text.Trim().Length < min.GetInt32()) errors.Add($"{path}: too short");
                if (schema.TryGetProperty("maxLength", out var max) && text.Length > max.GetInt32()) errors.Add($"{path}: too long");
                if (schema.TryGetProperty("pattern", out var pattern) && !Matches(text, pattern.GetString()!)) errors.Add($"{path}: invalid format");
                if (schema.TryGetProperty("enum", out var options) && !options.EnumerateArray().Any(o => o.GetString() == text)) errors.Add($"{path}: not an allowed value");
                break;

            case "integer":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number)) { errors.Add($"{path}: must be an integer"); return; }
                if (schema.TryGetProperty("minimum", out var minimum) && number < minimum.GetInt64()) errors.Add($"{path}: below minimum");
                if (schema.TryGetProperty("maximum", out var maximum) && number > maximum.GetInt64()) errors.Add($"{path}: above maximum");
                break;

            case "boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) errors.Add($"{path}: must be true or false");
                break;

            case "array":
                if (value.ValueKind != JsonValueKind.Array) { errors.Add($"{path}: must be an array"); return; }
                var count = value.GetArrayLength();
                if (schema.TryGetProperty("minItems", out var minItems) && count < minItems.GetInt32()) errors.Add($"{path}: too few items");
                if (schema.TryGetProperty("maxItems", out var maxItems) && count > maxItems.GetInt32()) { errors.Add($"{path}: too many items"); return; }
                if (schema.TryGetProperty("items", out var items))
                {
                    var index = 0;
                    foreach (var item in value.EnumerateArray()) Check(items, item, $"{path}[{index++}]", errors);
                }

                break;

            default:
                errors.Add($"{path}: unsupported schema");
                break;
        }
    }

    private static bool Matches(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, PatternTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string Join(string path, string name) => path == "$" ? name : $"{path}.{name}";
}
