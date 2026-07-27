namespace Kreyora.Application.Models;

/// <summary>
/// Frontend-safe error model aligned with RFC 7807.
/// Serialized to JSON and returned in API error responses.
/// </summary>
public sealed record ErrorDetail
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required int Status { get; init; }
    public string? Detail { get; init; }
    public string? TraceId { get; init; }
    public IDictionary<string, string[]>? Errors { get; init; }
}
