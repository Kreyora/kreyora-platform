using Kreyora.Domain.Abstractions;

namespace Kreyora.Infrastructure.Time;

public sealed class SystemTimeProvider : Domain.Abstractions.ITimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
