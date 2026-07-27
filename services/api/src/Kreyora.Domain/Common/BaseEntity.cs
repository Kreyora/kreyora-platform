using Kreyora.Domain.Abstractions;

namespace Kreyora.Domain.Common;

public abstract class BaseEntity
{
    public string Id { get; set; } = IdGenerator.NewId();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}
