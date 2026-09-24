using System.IO;
using AIBridge.Models;

namespace AIBridge.Services;

public class AntigravityRunner : ICodingAgentRunner
{
    private readonly ILogService _logService;

    public string Name => "Antigravity";

    public AntigravityRunner(ILogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public Task<(bool IsValid, string Message)> ValidateConfigurationAsync(string agentPath)
    {
        if (string.IsNullOrWhiteSpace(agentPath))
        {
            var msg = "Antigravity executable path is empty or not configured.";
            _logService.LogWarning(msg);
            return Task.FromResult((false, msg));
        }

        try
        {
            if (Directory.Exists(agentPath))
            {
                var msg = $"Antigravity path '{agentPath}' is a directory, not an executable file.";
                _logService.LogWarning(msg);
                return Task.FromResult((false, msg));
            }

            if (File.Exists(agentPath))
            {
                var msg = $"Antigravity executable file validated: {agentPath}";
                _logService.LogInfo(msg);
                return Task.FromResult((true, msg));
            }
            else
            {
                var msg = $"Antigravity executable file not found: {agentPath}";
                _logService.LogWarning(msg);
                return Task.FromResult((false, msg));
            }
        }
        catch (Exception ex)
        {
            var msg = $"Error validating Antigravity executable path: {ex.Message}";
            _logService.LogError(msg, ex);
            return Task.FromResult((false, msg));
        }
    }

    public Task<(bool IsValid, string Message)> ValidateWorkspaceAsync(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            var msg = "Workspace path is empty.";
            _logService.LogWarning(msg);
            return Task.FromResult((false, msg));
        }

        try
        {
            if (Directory.Exists(workspacePath))
            {
                var msg = $"Workspace path validated successfully: {workspacePath}";
                _logService.LogInfo(msg);
                return Task.FromResult((true, msg));
            }
            else
            {
                var msg = $"Workspace directory does not exist: {workspacePath}";
                _logService.LogWarning(msg);
                return Task.FromResult((false, msg));
            }
        }
        catch (Exception ex)
        {
            var msg = $"Error validating workspace path: {ex.Message}";
            _logService.LogError(msg, ex);
            return Task.FromResult((false, msg));
        }
    }

    public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        var startTime = DateTime.Now;

        _logService.LogWarning(
            $"[Phase 01 Skeleton] Execution requested for task {task.Id}. " +
            "Antigravity CLI execution is not implemented in Phase 01. Phase 02 will implement full CLI integration."
        );

        var result = new AgentResult
        {
            Success = false,
            ExitCode = -1,
            StartedAt = startTime,
            CompletedAt = DateTime.Now,
            StandardOutput = string.Empty,
            StandardError = "Phase 01 Notice: Antigravity CLI execution runner skeleton active. Phase 02 will implement CLI execution.",
            ErrorMessage = "Execution runner is not yet configured for CLI invocation (Phase 02 feature)."
        };

        return Task.FromResult(result);
    }
}
