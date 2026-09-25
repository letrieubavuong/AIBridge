using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public class AntigravityCodingAgent : ICodingAgent
{
    private readonly ITaskService _taskService;
    private readonly ICodingAgentRunner _antigravityRunner;
    private readonly IAntigravityEnvironmentService? _envService;

    public string Id => "antigravity";

    public CodingAgentDescriptor Descriptor { get; } = new()
    {
        Id = "antigravity",
        DisplayName = "Google Antigravity CLI",
        IsAvailable = true,
        IsConfigured = true,
        SupportsCancellation = true,
        SupportsWorkspace = true,
        SupportsStreamingOutput = true,
        SupportsStructuredPrompt = true,
        RequiresAuthentication = false,
        Version = "2.0.0"
    };

    public AntigravityCodingAgent(
        ITaskService taskService,
        ICodingAgentRunner antigravityRunner,
        IAntigravityEnvironmentService? envService = null)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _antigravityRunner = antigravityRunner ?? throw new ArgumentNullException(nameof(antigravityRunner));
        _envService = envService;
    }

    public async Task<CodingAgentExecutionResult> ExecuteAsync(
        CodingAgentExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sw = Stopwatch.StartNew();
        DateTime startTime = DateTime.Now;

        // Check environment availability if environment service available
        if (_envService != null)
        {
            var envInfo = _envService.CurrentInfo;
            if (envInfo.InstallationState == CliInstallationState.NotInstalled)
            {
                sw.Stop();
                return new CodingAgentExecutionResult
                {
                    ExecutionId = request.ExecutionId,
                    AgentId = Id,
                    Success = false,
                    ExitCode = -1,
                    ErrorCode = CodingAgentErrorCode.AgentUnavailable,
                    ErrorMessage = "Antigravity CLI is not installed or not found on PATH.",
                    StartedAt = startTime,
                    CompletedAt = DateTime.Now,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }

        // Reuse existing TaskService process runner architecture
        var agentTask = _taskService.CreateTask(request.Prompt, request.WorkspacePath);

        AgentResult result;
        try
        {
            result = await _taskService.SubmitTaskAsync(agentTask, _antigravityRunner, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new CodingAgentExecutionResult
            {
                ExecutionId = request.ExecutionId,
                AgentTaskId = agentTask.Id,
                AgentId = Id,
                Success = false,
                ExitCode = -1,
                ErrorCode = CodingAgentErrorCode.AgentCancelled,
                ErrorMessage = "Antigravity task execution was cancelled.",
                StartedAt = startTime,
                CompletedAt = DateTime.Now,
                DurationMs = sw.ElapsedMilliseconds,
                WasCancelled = true
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CodingAgentExecutionResult
            {
                ExecutionId = request.ExecutionId,
                AgentTaskId = agentTask.Id,
                AgentId = Id,
                Success = false,
                ExitCode = -1,
                ErrorCode = CodingAgentErrorCode.AgentExecutionFailed,
                ErrorMessage = ex.Message,
                StartedAt = startTime,
                CompletedAt = DateTime.Now,
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        sw.Stop();
        DateTime endTime = DateTime.Now;

        bool isPermissionDenied = result.ErrorMessage != null &&
            (result.ErrorMessage.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
             result.ErrorMessage.Contains("access denied", StringComparison.OrdinalIgnoreCase));

        string errorCode = result.Success ? string.Empty :
            (isPermissionDenied ? CodingAgentErrorCode.AgentPermissionDenied : CodingAgentErrorCode.AgentExecutionFailed);

        return new CodingAgentExecutionResult
        {
            ExecutionId = request.ExecutionId,
            AgentTaskId = agentTask.Id,
            AgentId = Id,
            Success = result.Success,
            ExitCode = result.ExitCode,
            Stdout = result.StandardOutput ?? string.Empty,
            Stderr = result.StandardError ?? string.Empty,
            ErrorCode = errorCode,
            ErrorMessage = result.ErrorMessage ?? string.Empty,
            StartedAt = startTime,
            CompletedAt = endTime,
            DurationMs = sw.ElapsedMilliseconds,
            PermissionDenied = isPermissionDenied
        };
    }
}
