using System.Diagnostics.CodeAnalysis;
using Kreyora.Application.Integrations;
using Kreyora.Domain.Integrations;

namespace Kreyora.Infrastructure.Integrations;

public sealed class ChannelProviderRegistry : IChannelProviderRegistry
{
    private readonly Dictionary<ChannelType, IChannelProvider> providers;

    public ChannelProviderRegistry(IEnumerable<IChannelProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers.ToDictionary(p => p.Channel);
    }

    public IChannelProvider GetProvider(ChannelType channel)
    {
        if (!providers.TryGetValue(channel, out var provider))
        {
            throw new NotSupportedException($"Channel provider for '{channel}' is not registered or supported.");
        }

        return provider;
    }

    public bool TryGetProvider(ChannelType channel, [NotNullWhen(true)] out IChannelProvider? provider)
    {
        return providers.TryGetValue(channel, out provider);
    }
}

