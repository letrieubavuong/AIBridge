using AIBridge.Models;

namespace AIBridge.Services;

public interface ITaskService
{
    AgentTask? CurrentTask { get; }
    event Action<AgentTask?>? CurrentTaskChanged;
    event Action<AgentTask>? TaskUpdated;
    
    AgentTask CreateTask(string prompt, string workspacePath, string? configuredAgentPath = null);

    Task<AgentResult> SubmitTaskAsync(AgentTask task, ICodingAgentRunner runner, CancellationToken cancellationToken = default);
    void CancelCurrentTask();
}

