using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IAIBrainService
{
    Task<BrainResponse> AnalyzeAsync(BrainRequest request, CancellationToken cancellationToken = default);
    Task<bool> TestActiveProviderAsync(CancellationToken cancellationToken = default);
    BrainState GetCurrentState();
    BrainProviderDescriptor? GetActiveProviderDescriptor();
    IReadOnlyList<BrainDecisionRecord> GetDecisionHistory();
}
