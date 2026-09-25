using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface ICodingAgentService
{
    IReadOnlyList<TaskPlan> GetDispatchableTasks(ProjectPlan plan);
    TaskPlan? GetNextDispatchableTask(ProjectPlan plan);

    bool IsTaskDispatchable(
        ProjectPlan plan,
        string phaseId,
        string taskId,
        ExecutionPromptPackage? promptPackage,
        out string errorReason,
        out string errorCode);

    Task<CodingAgentExecutionResult> DispatchTaskAsync(
        ProjectPlan plan,
        string phaseId,
        string taskId,
        ExecutionPromptPackage promptPackage,
        string workspacePath = "",
        int timeoutSeconds = 600,
        CancellationToken cancellationToken = default);

    void CancelCurrentExecution();
    TaskExecutionRecord? GetCurrentExecution();
    IReadOnlyList<TaskExecutionRecord> GetExecutionHistory();
    ExecutionReviewPackage? GetReviewPackage(string executionId);
}
