using System.Diagnostics.CodeAnalysis;
using Kreyora.Domain.Integrations;

namespace Kreyora.Application.Integrations;

public interface IChannelProviderRegistry
{
    IChannelProvider GetProvider(ChannelType channel);
    bool TryGetProvider(ChannelType channel, [NotNullWhen(true)] out IChannelProvider? provider);
}

