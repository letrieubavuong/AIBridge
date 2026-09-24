using System.ComponentModel;
using AIBridge.Models;

namespace AIBridge.Services;

public class TaskService : ITaskService
{
    private readonly ILogService _logService;
    private readonly ITaskRegistry? _taskRegistry;
    private readonly SemaphoreSlim _executionSemaphore = new(1, 1);
    private AgentTask? _currentTask;
    private CancellationTokenSource? _currentCts;

    public event Action<AgentTask?>? CurrentTaskChanged;
    public event Action<AgentTask>? TaskUpdated;

    public AgentTask? CurrentTask
    {
        get => _currentTask;
        private set
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
            if (_currentTask != null)
            {
                TaskUpdated?.Invoke(_currentTask);
            }
        }
    }

    public TaskService(ILogService logService, ITaskRegistry? taskRegistry = null)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _taskRegistry = taskRegistry;
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
            CurrentTaskChanged?.Invoke(task);
        }
    }

    public AgentTask CreateTask(string prompt, string workspacePath, string? configuredAgentPath = null)
    {
        var task = new AgentTask
        {
            Prompt = prompt,
            WorkspacePath = workspacePath,
            ConfiguredAgentPath = configuredAgentPath,
            Status = AgentTaskStatus.Pending,
            CreatedAt = DateTime.Now
        };
        _logService.LogInfo($"New agent task created with ID: {task.Id} (Status: Pending)");
        _taskRegistry?.RegisterTask(task);
        return task;
    }

    public async Task<AgentResult> SubmitTaskAsync(AgentTask task, ICodingAgentRunner runner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(runner);

        CurrentTask = task;
        task.StartedAt = DateTime.Now;
        task.Status = AgentTaskStatus.Running;
        _logService.LogInfo($"Task {task.Id} status transition -> Running with runner '{runner.Name}'.");

        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        AgentResult result;

        try
        {
            result = await runner.ExecuteAsync(task, _currentCts.Token);
            
            task.CompletedAt = DateTime.Now;
            if (result.Success)
            {
                task.Status = AgentTaskStatus.Success;
                _logService.LogInfo($"Task {task.Id} status transition -> Success.");
            }
            else
            {
                task.Status = AgentTaskStatus.Failed;
                _logService.LogWarning($"Task {task.Id} status transition -> Failed: {result.ErrorMessage}");
            }
        }
        catch (OperationCanceledException)
        {
            task.CompletedAt = DateTime.Now;
            task.Status = AgentTaskStatus.Cancelled;
            _logService.LogWarning($"Task {task.Id} status transition -> Cancelled.");

            result = new AgentResult
            {
                Success = false,
                ExitCode = -1,
                StartedAt = task.StartedAt ?? DateTime.Now,
                CompletedAt = DateTime.Now,
                ErrorMessage = "Task execution was cancelled by user."
            };
        }
        catch (Exception ex)
        {
            task.CompletedAt = DateTime.Now;
            task.Status = AgentTaskStatus.Failed;
            _logService.LogError($"Unexpected error while executing task {task.Id}", ex);

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

        _taskRegistry?.RecordResult(task.Id, result);
        return result;
    }

    public void CancelCurrentTask()
    {
        if (_currentTask != null && _currentTask.Status == AgentTaskStatus.Running)
        {
            _logService.LogInfo($"Cancellation requested for task {_currentTask.Id}.");
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
