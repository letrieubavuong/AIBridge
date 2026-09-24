using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface ICodingAgent
{
    string Id { get; }
    CodingAgentDescriptor Descriptor { get; }

    Task<CodingAgentExecutionResult> ExecuteAsync(
        CodingAgentExecutionRequest request,
        CancellationToken cancellationToken = default);
}
