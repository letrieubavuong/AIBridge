using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public class CodingAgentService : ICodingAgentService
{
    private readonly ICodingAgentRegistry _agentRegistry;
    private readonly ITaskService _taskService;
    private readonly IProjectPlanStore _planStore;
    private readonly IPromptValidator _promptValidator;
    private readonly IGitEvidenceService? _gitEvidenceService;
    private readonly ITaskRegistry? _taskRegistry;
    private readonly IHumanApprovalService? _humanApprovalService;
    private readonly ILogService? _logService;

    private readonly List<TaskExecutionRecord> _executionHistory = new();
    private readonly Dictionary<string, ExecutionReviewPackage> _reviewPackages = new(StringComparer.OrdinalIgnoreCase);
    private TaskExecutionRecord? _currentExecutionRecord;
    private readonly object _lock = new();

    public CodingAgentService(
        ICodingAgentRegistry agentRegistry,
        ITaskService taskService,
        IProjectPlanStore planStore,
        IPromptValidator promptValidator,
        IGitEvidenceService? gitEvidenceService = null,
        ITaskRegistry? taskRegistry = null,
        IHumanApprovalService? humanApprovalService = null,
        ILogService? logService = null)
    {
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
        _promptValidator = promptValidator ?? throw new ArgumentNullException(nameof(promptValidator));
        _gitEvidenceService = gitEvidenceService;
        _taskRegistry = taskRegistry;
        _humanApprovalService = humanApprovalService;
        _logService = logService;
    }

    public IReadOnlyList<TaskPlan> GetDispatchableTasks(ProjectPlan plan)
    {
        if (plan == null || (plan.Status != ProjectPlanStatus.Approved && plan.Status != ProjectPlanStatus.Active))
        {
            return Array.Empty<TaskPlan>();
        }

        var list = new List<TaskPlan>();
        foreach (var phase in plan.Phases.OrderBy(p => p.PhaseNumber))
        {
            if (!ArePhaseDependenciesSatisfied(plan, phase))
            {
                continue;
            }

            foreach (var task in phase.Tasks.OrderBy(t => t.TaskNumber))
            {
                if (IsTaskDispatchableInternal(plan, phase, task, null, out _, out _))
                {
                    list.Add(task);
                }
            }
        }

        return list;
    }

    public TaskPlan? GetNextDispatchableTask(ProjectPlan plan)
    {
        return GetDispatchableTasks(plan).FirstOrDefault();
    }

    public bool IsTaskDispatchable(
        ProjectPlan plan,
        string phaseId,
        string taskId,
        ExecutionPromptPackage? promptPackage,
        out string errorReason,
        out string errorCode)
    {
        errorReason = string.Empty;
        errorCode = string.Empty;

        if (plan == null)
        {
            errorReason = "Project plan is null.";
            errorCode = CodingAgentErrorCode.TaskNotDispatchable;
            return false;
        }

        if (plan.Status != ProjectPlanStatus.Approved && plan.Status != ProjectPlanStatus.Active)
        {
            errorReason = $"Project plan is in '{plan.Status}' status. Dispatch requires Approved or Active status.";
            errorCode = CodingAgentErrorCode.TaskNotDispatchable;
            return false;
        }

        var phase = plan.Phases.FirstOrDefault(p => string.Equals(p.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase));
        if (phase == null)
        {
            errorReason = $"Phase '{phaseId}' not found in project plan.";
            errorCode = CodingAgentErrorCode.TaskNotDispatchable;
            return false;
        }

        var task = phase.Tasks.FirstOrDefault(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        if (task == null)
        {
            errorReason = $"Task '{taskId}' not found in phase '{phaseId}'.";
            errorCode = CodingAgentErrorCode.TaskNotDispatchable;
            return false;
        }

        return IsTaskDispatchableInternal(plan, phase, task, promptPackage, out errorReason, out errorCode);
    }

    private bool IsTaskDispatchableInternal(
        ProjectPlan plan,
        PhasePlan phase,
        TaskPlan task,
        ExecutionPromptPackage? promptPackage,
        out string errorReason,
        out string errorCode)
    {
        errorReason = string.Empty;
        errorCode = string.Empty;

        if (task.Status == TaskPlanStatus.Passed)
        {
            errorReason = $"Task '{task.TaskId}' is already Passed.";
            errorCode = CodingAgentErrorCode.TaskNotDispatchable;
            return false;
        }

        if (task.Status == TaskPlanStatus.Running)
        {
            errorReason = $"Task '{task.TaskId}' is currently Running.";
            errorCode = CodingAgentErrorCode.TaskNotDispatchable;
            return false;
        }

        if (task.Status == TaskPlanStatus.Blocked)
        {
            errorReason = $"Task '{task.TaskId}' is currently Blocked.";
            errorCode = CodingAgentErrorCode.TaskNotDispatchable;
            return false;
        }

        if (!ArePhaseDependenciesSatisfied(plan, phase))
        {
            errorReason = $"Phase '{phase.PhaseId}' dependencies are not satisfied.";
            errorCode = CodingAgentErrorCode.DependencyNotSatisfied;
            return false;
        }

        if (!AreTaskDependenciesSatisfied(plan, task))
        {
            errorReason = $"Task '{task.TaskId}' dependencies are not satisfied.";
            errorCode = CodingAgentErrorCode.DependencyNotSatisfied;
            return false;
        }

        if (task.RequiresHumanApproval)
        {
            var approval = _humanApprovalService?.GetValidApprovalAsync(plan.ProjectId, phase.PhaseId, task.TaskId, plan.Version).GetAwaiter().GetResult();
            if (approval == null || !approval.IsValid)
            {
                errorReason = $"Task '{task.TaskId}' requires explicit human approval before dispatch.";
                errorCode = CodingAgentErrorCode.HumanApprovalRequired;
                return false;
            }
        }

        if (promptPackage != null)
        {
            var promptVal = _promptValidator.Validate(promptPackage, plan);
            if (!promptVal.IsValid)
            {
                errorReason = string.Join("; ", promptVal.Errors);
                errorCode = promptVal.Errors.Any(e => e.Contains("PROMPT_STALE"))
                    ? CodingAgentErrorCode.PromptStale
                    : CodingAgentErrorCode.PromptInvalid;
                return false;
            }

            if (!string.Equals(promptPackage.TaskId, task.TaskId, StringComparison.OrdinalIgnoreCase))
            {
                errorReason = $"Prompt package TaskId '{promptPackage.TaskId}' does not match requested TaskId '{task.TaskId}'.";
                errorCode = CodingAgentErrorCode.PromptInvalid;
                return false;
            }
        }

        return true;
    }

    private static bool ArePhaseDependenciesSatisfied(ProjectPlan plan, PhasePlan phase)
    {
        if (phase.Dependencies == null || phase.Dependencies.Count == 0) return true;

        foreach (var depPhaseId in phase.Dependencies)
        {
            var depPhase = plan.Phases.FirstOrDefault(p => string.Equals(p.PhaseId, depPhaseId, StringComparison.OrdinalIgnoreCase));
            if (depPhase == null || depPhase.Status != PhaseStatus.Completed)
            {
                return false;
            }
        }
        return true;
    }

    private static bool AreTaskDependenciesSatisfied(ProjectPlan plan, TaskPlan task)
    {
        if (task.Dependencies == null || task.Dependencies.Count == 0) return true;

        var allTasks = plan.Phases.SelectMany(p => p.Tasks).ToDictionary(t => t.TaskId, StringComparer.OrdinalIgnoreCase);

        foreach (var depTaskId in task.Dependencies)
        {
            if (!allTasks.TryGetValue(depTaskId, out var depTask) || depTask.Status != TaskPlanStatus.Passed)
            {
                return false;
            }
        }
        return true;
    }

    public async Task<CodingAgentExecutionResult> DispatchTaskAsync(
        ProjectPlan plan,
        string phaseId,
        string taskId,
        ExecutionPromptPackage promptPackage,
        string workspacePath = "",
        int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(promptPackage);

        if (!IsTaskDispatchable(plan, phaseId, taskId, promptPackage, out string errorReason, out string errorCode))
        {
            return new CodingAgentExecutionResult
            {
                ExecutionId = Guid.NewGuid().ToString("N"),
                AgentId = _agentRegistry.GetSelectedAgent().Id,
                Success = false,
                ExitCode = -1,
                ErrorCode = errorCode,
                ErrorMessage = errorReason,
                StartedAt = DateTime.Now,
                CompletedAt = DateTime.Now
            };
        }

        var phase = plan.Phases.First(p => string.Equals(p.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase));
        var task = phase.Tasks.First(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase));

        // Consume human approval upon starting dispatch
        if (task.RequiresHumanApproval && _humanApprovalService != null)
        {
            var approval = await _humanApprovalService.GetValidApprovalAsync(plan.ProjectId, phase.PhaseId, task.TaskId, plan.Version, cancellationToken);
            if (approval != null)
            {
                await _humanApprovalService.ConsumeApprovalAsync(approval.ApprovalId, cancellationToken);
            }
        }

        if (!_taskService.TryAcquireExecutionSlot())
        {
            return new CodingAgentExecutionResult
            {
                ExecutionId = Guid.NewGuid().ToString("N"),
                AgentId = _agentRegistry.GetSelectedAgent().Id,
                Success = false,
                ExitCode = -1,
                ErrorCode = CodingAgentErrorCode.ConcurrencyConflict,
                ErrorMessage = "Another task or agent execution is currently running. Only one execution is allowed at a time.",
                StartedAt = DateTime.Now,
                CompletedAt = DateTime.Now
            };
        }

        var agent = _agentRegistry.GetSelectedAgent();

        string targetWorkspace = !string.IsNullOrWhiteSpace(workspacePath)
            ? workspacePath
            : (string.IsNullOrWhiteSpace(promptPackage.WorkspaceContext) ? Directory.GetCurrentDirectory() : promptPackage.WorkspaceContext);

        if (!Directory.Exists(targetWorkspace))
        {
            _taskService.ReleaseExecutionSlot();
            return new CodingAgentExecutionResult
            {
                ExecutionId = Guid.NewGuid().ToString("N"),
                AgentId = agent.Id,
                Success = false,
                ExitCode = -1,
                ErrorCode = CodingAgentErrorCode.WorkspaceInvalid,
                ErrorMessage = $"Workspace directory '{targetWorkspace}' does not exist.",
                StartedAt = DateTime.Now,
                CompletedAt = DateTime.Now
            };
        }

        var req = new CodingAgentExecutionRequest
        {
            ExecutionId = Guid.NewGuid().ToString("N"),
            PromptId = promptPackage.PromptId,
            ProjectId = plan.ProjectId,
            PhaseId = phase.PhaseId,
            TaskId = task.TaskId,
            WorkspacePath = targetWorkspace,
            Prompt = promptPackage.GeneratedPrompt,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            PlanVersion = plan.Version,
            RequestedAt = DateTime.Now
        };

        int attemptNumber;
        lock (_lock)
        {
            attemptNumber = Math.Max(task.RetryCount + 1, _executionHistory.Count(e => string.Equals(e.TaskId, task.TaskId, StringComparison.OrdinalIgnoreCase)) + 1);
        }

        var record = new TaskExecutionRecord
        {
            ExecutionId = req.ExecutionId,
            PromptId = req.PromptId,
            ProjectId = req.ProjectId,
            PhaseId = req.PhaseId,
            TaskId = req.TaskId,
            PlanVersion = req.PlanVersion,
            AgentId = agent.Id,
            StartedAt = req.RequestedAt,
            AttemptNumber = attemptNumber
        };

        lock (_lock)
        {
            _currentExecutionRecord = record;
        }

        task.Status = TaskPlanStatus.Running;
        if (plan.Status == ProjectPlanStatus.Approved)
        {
            plan.Status = ProjectPlanStatus.Active;
        }
        await _planStore.SavePlanAsync(plan);

        _logService?.LogInfo($"Dispatching Task {task.TaskId} to coding agent '{agent.Id}'...");

        using var timeoutCts = new CancellationTokenSource(req.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        CodingAgentExecutionResult result;
        try
        {
            result = await agent.ExecuteAsync(req, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            bool isTimeout = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;

            result = new CodingAgentExecutionResult
            {
                ExecutionId = req.ExecutionId,
                AgentId = agent.Id,
                Success = false,
                ExitCode = -1,
                ErrorCode = isTimeout ? CodingAgentErrorCode.AgentTimeout : CodingAgentErrorCode.AgentCancelled,
                ErrorMessage = isTimeout ? $"Execution timed out after {timeoutSeconds} seconds." : "Execution was cancelled.",
                StartedAt = req.RequestedAt,
                CompletedAt = DateTime.Now,
                WasCancelled = !isTimeout,
                TimedOut = isTimeout
            };
        }
        catch (Exception ex)
        {
            _logService?.LogError($"Error executing coding agent task {task.TaskId}", ex);
            result = new CodingAgentExecutionResult
            {
                ExecutionId = req.ExecutionId,
                AgentId = agent.Id,
                Success = false,
                ExitCode = -1,
                ErrorCode = CodingAgentErrorCode.AgentExecutionFailed,
                ErrorMessage = ex.Message,
                StartedAt = req.RequestedAt,
                CompletedAt = DateTime.Now
            };
        }
        finally
        {
            _taskService.ReleaseExecutionSlot();
        }

        record.AgentTaskId = result.AgentTaskId;

        // Fetch Git Evidence correlated with exact AgentTaskId / ExecutionId
        GitEvidence? evidence = null;
        if (!string.IsNullOrWhiteSpace(result.AgentTaskId))
        {
            evidence = _taskRegistry?.GetGitEvidence(result.AgentTaskId);
        }
        if (evidence == null && !string.IsNullOrWhiteSpace(req.ExecutionId))
        {
            evidence = _taskRegistry?.GetGitEvidence(req.ExecutionId);
        }
        if (evidence == null && !string.IsNullOrWhiteSpace(task.TaskId))
        {
            evidence = _taskRegistry?.GetGitEvidence(task.TaskId);
        }

        // Update task status based on result
        // CRITICAL REQUIREMENT: Successful execution moves to Reviewing (NOT Passed)!
        if (result.Success)
        {
            task.Status = TaskPlanStatus.Reviewing;
            _logService?.LogInfo($"Task '{task.TaskId}' agent execution SUCCESS. Status set to Reviewing.");
        }
        else
        {
            task.Status = TaskPlanStatus.Failed;
            task.RetryCount++;
            _logService?.LogError($"Task '{task.TaskId}' agent execution FAILED: {result.ErrorMessage}");
        }

        await _planStore.SavePlanAsync(plan);

        record.CompletedAt = result.CompletedAt;
        record.AgentExecutionResult = result;
        record.GitEvidence = evidence;

        // Construct ExecutionReviewPackage for ChatGPT Web evidence review
        var reviewPackage = new ExecutionReviewPackage
        {
            ExecutionId = result.ExecutionId,
            ProjectId = plan.ProjectId,
            PhaseId = phase.PhaseId,
            TaskId = task.TaskId,
            PlanVersion = plan.Version,
            TaskTitle = task.Title,
            TaskObjective = task.Objective,
            AcceptanceCriteria = new List<string>(promptPackage.AcceptanceCriteria),
            AttemptNumber = record.AttemptNumber,
            AgentId = agent.Id,
            AgentSuccess = result.Success,
            ExitCode = result.ExitCode,
            OutputSummary = !string.IsNullOrWhiteSpace(result.Stdout) ? result.Stdout : result.ErrorMessage,
            ErrorMessage = result.ErrorMessage,
            GitEvidence = evidence,
            CommitSha = evidence?.NewCommitSha ?? string.Empty,
            CommitMessage = evidence?.NewCommitMessage ?? string.Empty,
            ChangedFiles = evidence?.ChangedFiles?.Select(f => f.Path).ToList() ?? new List<string>(),
            Diff = evidence?.Diff ?? string.Empty,
            DiffTruncated = evidence?.DiffTruncated ?? false,
            ExecutionStartedAt = result.StartedAt,
            ExecutionCompletedAt = result.CompletedAt,
            ReviewStatus = "Pending"
        };

        lock (_lock)
        {
            _currentExecutionRecord = null;
            _executionHistory.Add(record);
            _reviewPackages[result.ExecutionId] = reviewPackage;

            if (_executionHistory.Count > 100)
            {
                _executionHistory.RemoveAt(0);
            }
        }

        return result;
    }

    public void CancelCurrentExecution()
    {
        _taskService.CancelCurrentTask();
    }

    public TaskExecutionRecord? GetCurrentExecution()
    {
        lock (_lock)
        {
            return _currentExecutionRecord;
        }
    }

    public IReadOnlyList<TaskExecutionRecord> GetExecutionHistory()
    {
        lock (_lock)
        {
            return _executionHistory.ToList();
        }
    }

    public ExecutionReviewPackage? GetReviewPackage(string executionId)
    {
        if (string.IsNullOrWhiteSpace(executionId)) return null;

        lock (_lock)
        {
            if (_reviewPackages.TryGetValue(executionId, out var pkg))
            {
                return pkg;
            }

            var record = _executionHistory.FirstOrDefault(e => string.Equals(e.ExecutionId, executionId, StringComparison.OrdinalIgnoreCase));
            if (record != null && record.AgentExecutionResult != null)
            {
                var result = record.AgentExecutionResult;
                var evidence = record.GitEvidence;
                return new ExecutionReviewPackage
                {
                    ExecutionId = result.ExecutionId,
                    ProjectId = record.ProjectId,
                    PhaseId = record.PhaseId,
                    TaskId = record.TaskId,
                    PlanVersion = record.PlanVersion,
                    AttemptNumber = record.AttemptNumber,
                    AgentId = record.AgentId,
                    AgentSuccess = result.Success,
                    ExitCode = result.ExitCode,
                    OutputSummary = !string.IsNullOrWhiteSpace(result.Stdout) ? result.Stdout : result.ErrorMessage,
                    ErrorMessage = result.ErrorMessage,
                    GitEvidence = evidence,
                    CommitSha = evidence?.NewCommitSha ?? string.Empty,
                    CommitMessage = evidence?.NewCommitMessage ?? string.Empty,
                    ChangedFiles = evidence?.ChangedFiles?.Select(f => f.Path).ToList() ?? new List<string>(),
                    Diff = evidence?.Diff ?? string.Empty,
                    DiffTruncated = evidence?.DiffTruncated ?? false,
                    ExecutionStartedAt = result.StartedAt,
                    ExecutionCompletedAt = result.CompletedAt,
                    ReviewStatus = "Pending"
                };
            }

            return null;
        }
    }
}
