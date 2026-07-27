namespace Kreyora.Infrastructure.Persistence.Entities;

public sealed class IdempotencyRecord
{
    public required string IdempotencyKey { get; set; }
    public required string ConsumerName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
