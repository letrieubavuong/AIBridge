using System.Collections.Generic;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IAIBrainProviderRegistry
{
    IReadOnlyList<BrainProviderDescriptor> GetAvailableProviders();
    IAIBrainProvider? GetProvider(string providerId);
    void RegisterProvider(IAIBrainProvider provider);
}
