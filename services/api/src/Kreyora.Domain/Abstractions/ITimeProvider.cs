namespace Kreyora.Domain.Abstractions;

public interface ITimeProvider
{
    DateTimeOffset UtcNow { get; }
}
