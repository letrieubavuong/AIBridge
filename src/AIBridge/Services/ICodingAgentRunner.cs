using AIBridge.Models;

namespace AIBridge.Services;

public interface ICodingAgentRunner
{
    string Name { get; }
    Task<(bool IsValid, string Message)> ValidateConfigurationAsync(string agentPath);
    Task<(bool IsValid, string Message)> ValidateWorkspaceAsync(string workspacePath);
    Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken cancellationToken = default);
}
