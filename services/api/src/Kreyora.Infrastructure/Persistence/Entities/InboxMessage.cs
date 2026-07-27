using Kreyora.Domain.Abstractions;

namespace Kreyora.Infrastructure.Persistence.Entities;

public sealed class InboxMessage
{
    public string Id { get; set; } = IdGenerator.NewId();
    public required string MessageId { get; set; }
    public required string ConsumerName { get; set; }
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}
