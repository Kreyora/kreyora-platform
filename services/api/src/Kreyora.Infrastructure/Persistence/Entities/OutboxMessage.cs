using Kreyora.Domain.Abstractions;

namespace Kreyora.Infrastructure.Persistence.Entities;

public sealed class OutboxMessage
{
    public string Id { get; set; } = IdGenerator.NewId();
    public required string Type { get; set; }
    public required string Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
}
