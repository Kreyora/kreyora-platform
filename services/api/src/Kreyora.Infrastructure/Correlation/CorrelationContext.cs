using Kreyora.Application.Abstractions;

namespace Kreyora.Infrastructure.Correlation;

public sealed class CorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = Guid.NewGuid().ToString("D");

    public void SetCorrelationId(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        CorrelationId = correlationId;
    }
}
