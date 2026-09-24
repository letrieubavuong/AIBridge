using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IAIBrain
{
    Task<BrainResponse> AnalyzeAsync(BrainRequest request, CancellationToken cancellationToken = default);
}
