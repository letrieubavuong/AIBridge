using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public class TaskService : ITaskService
{
    private readonly ILogService _logService;
    private readonly ITaskRegistry? _taskRegistry;
    private readonly IGitEvidenceService? _gitEvidenceService;
    private readonly IConfigService? _configService;
    private readonly SemaphoreSlim _executionSemaphore = new(1, 1);
    private AgentTask? _currentTask;
    private CancellationTokenSource? _currentCts;

    public AgentTask? CurrentTask
    {
        get => _currentTask;
        private set
        {
            if (_currentTask != value)
            {
                if (_currentTask != null)
                {
                    _currentTask.PropertyChanged -= OnTaskPropertyChanged;
                }

                _currentTask = value;

                if (_currentTask != null)
                {
                    _currentTask.PropertyChanged += OnTaskPropertyChanged;
                }

                CurrentTaskChanged?.Invoke(_currentTask);
            }
        }
    }

    public event Action<AgentTask?>? CurrentTaskChanged;
    public event Action<AgentTask>? TaskUpdated;

    public TaskService(
        ILogService logService, 
        ITaskRegistry? taskRegistry = null,
        IGitEvidenceService? gitEvidenceService = null,
        IConfigService? configService = null)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _taskRegistry = taskRegistry;
        _gitEvidenceService = gitEvidenceService;
        _configService = configService;
    }

    public bool TryAcquireExecutionSlot()
    {
        return _executionSemaphore.Wait(0);
    }

    public void ReleaseExecutionSlot()
    {
        if (_executionSemaphore.CurrentCount == 0)
        {
            _executionSemaphore.Release();
        }
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AgentTask task)
        {
            TaskUpdated?.Invoke(task);
        }
    }

    public AgentTask CreateTask(string prompt, string workspacePath, string? agentPath = null)
    {
        var task = new AgentTask
        {
            Prompt = prompt,
            WorkspacePath = workspacePath,
            ConfiguredAgentPath = agentPath ?? string.Empty,
            Status = AgentTaskStatus.Pending,
            CreatedAt = DateTime.Now
        };

        _taskRegistry?.RegisterTask(task);
        return task;
    }

    public async Task<AgentResult> SubmitTaskAsync(AgentTask task, ICodingAgentRunner runner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(runner);

        CurrentTask = task;
        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _logService.LogInfo($"Submitting task '{task.Id}' with prompt: \"{task.Prompt}\"");

        task.StartedAt = DateTime.Now;
        task.Status = AgentTaskStatus.Running;

        GitSnapshot? beforeSnapshot = null;
        var config = _configService?.LoadConfig();

        if (_gitEvidenceService != null && !string.IsNullOrWhiteSpace(task.WorkspacePath))
        {
            try
            {
                beforeSnapshot = await _gitEvidenceService.CaptureSnapshotAsync(task.WorkspacePath, config?.GitPath, _currentCts.Token);
            }
            catch (Exception ex)
            {
                _logService.LogWarning($"Failed to capture Git before-snapshot: {ex.Message}");
            }
        }

        AgentResult result;
        AgentTaskStatus finalStatus;
        try
        {
            result = await runner.ExecuteAsync(task, _currentCts.Token);
            finalStatus = result.Success ? AgentTaskStatus.Success : AgentTaskStatus.Failed;
        }
        catch (OperationCanceledException)
        {
            finalStatus = AgentTaskStatus.Cancelled;
            result = new AgentResult
            {
                Success = false,
                ExitCode = -1,
                StartedAt = task.StartedAt ?? DateTime.Now,
                CompletedAt = DateTime.Now,
                ErrorMessage = "Task execution was cancelled."
            };
        }
        catch (Exception ex)
        {
            finalStatus = AgentTaskStatus.Failed;
            _logService.LogError($"Unexpected error executing task '{task.Id}'", ex);

            result = new AgentResult
            {
                Success = false,
                ExitCode = -1,
                StartedAt = task.StartedAt ?? DateTime.Now,
                CompletedAt = DateTime.Now,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
            ReleaseExecutionSlot();
        }

        if (_gitEvidenceService != null && !string.IsNullOrWhiteSpace(task.WorkspacePath))
        {
            try
            {
                var afterSnapshot = await _gitEvidenceService.CaptureSnapshotAsync(task.WorkspacePath, config?.GitPath, CancellationToken.None);
                var evidence = await _gitEvidenceService.CalculateEvidenceAsync(task.Id, task.WorkspacePath, beforeSnapshot, afterSnapshot, config?.GitPath, CancellationToken.None);

                _taskRegistry?.RecordGitEvidence(task.Id, evidence);

                if (config != null && config.GitAutoPush && result.Success && evidence.IsRepository && evidence.NewCommitDetected)
                {
                    _logService.LogInfo("GitAutoPush enabled: Attempting automatic push...");
                    await _gitEvidenceService.PushTaskCommitAsync(task.Id, task.WorkspacePath, config, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logService.LogWarning($"Failed to calculate Git evidence: {ex.Message}");
            }
        }

        task.CompletedAt = DateTime.Now;
        task.Status = finalStatus;

        _taskRegistry?.RecordResult(task.Id, result);

        if (finalStatus == AgentTaskStatus.Success)
        {
            _logService.LogInfo($"Task '{task.Id}' completed successfully.");
        }
        else if (finalStatus == AgentTaskStatus.Failed)
        {
            _logService.LogError($"Task '{task.Id}' failed: {result.ErrorMessage}");
        }
        else
        {
            _logService.LogWarning($"Task '{task.Id}' was cancelled.");
        }

        return result;
    }

    public void CancelCurrentTask()
    {
        if (_currentTask != null && _currentTask.Status == AgentTaskStatus.Running)
        {
            _logService.LogInfo($"Cancelling current task '{_currentTask.Id}'...");
            _currentCts?.Cancel();
        }
        else
        {
            _logService.LogWarning("No running task to cancel.");
        }
    }

    public async Task<bool> WaitForCurrentTaskToCompleteAsync(TimeSpan timeout)
    {
        if (_currentTask == null || _currentTask.Status != AgentTaskStatus.Running)
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>();
        Action<AgentTask> handler = null!;
        handler = task =>
        {
            if (task.Id == _currentTask?.Id && task.Status != AgentTaskStatus.Running)
            {
                TaskUpdated -= handler;
                tcs.TrySetResult(true);
            }
        };

        TaskUpdated += handler;

        // Double-check if completed before handler subscription
        if (_currentTask == null || _currentTask.Status != AgentTaskStatus.Running)
        {
            TaskUpdated -= handler;
            return true;
        }

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        TaskUpdated -= handler;

        return completedTask == tcs.Task;
    }
}
