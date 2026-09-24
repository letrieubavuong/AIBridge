using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IExecutionPromptService
{
    Task<ExecutionPromptPackage> PreparePromptPackageAsync(
        ProjectPlan plan,
        string phaseId,
        string taskId,
        string externalInstructions = "",
        List<string>? verificationInstructions = null,
        string commitInstructions = "",
        string workspacePath = "",
        string generatedBy = "ChatGPTWeb",
        CancellationToken cancellationToken = default);

    ExecutionPromptPackage? GetPromptPackage(string promptId);
    IReadOnlyList<ExecutionPromptPackage> GetPromptHistory();
}
