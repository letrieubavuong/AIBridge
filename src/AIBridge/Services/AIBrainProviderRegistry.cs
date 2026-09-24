using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AIBridge.Models;

namespace AIBridge.Services;

public class AIBrainProviderRegistry : IAIBrainProviderRegistry
{
    private readonly ConcurrentDictionary<string, IAIBrainProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterProvider(IAIBrainProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        var id = provider.Descriptor.Id;
        _providers[id] = provider;
    }

    public IReadOnlyList<BrainProviderDescriptor> GetAvailableProviders()
    {
        return _providers.Values.Select(p => p.Descriptor).ToList().AsReadOnly();
    }

    public IAIBrainProvider? GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;
        _providers.TryGetValue(providerId.Trim(), out var provider);
        return provider;
    }
}
