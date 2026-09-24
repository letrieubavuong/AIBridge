using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IAIBrainProvider : IAIBrain
{
    BrainProviderDescriptor Descriptor { get; }
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
