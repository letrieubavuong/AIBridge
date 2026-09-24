using System.ComponentModel;
using AIBridge.Models;

namespace AIBridge.Services;

public class TaskService : ITaskService
{
    private readonly ILogService _logService;
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

    public TaskService(ILogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
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

        try
        {
            var result = await runner.ExecuteAsync(task, _currentCts.Token);
            
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

            return result;
        }
        catch (OperationCanceledException)
        {
            task.CompletedAt = DateTime.Now;
            task.Status = AgentTaskStatus.Cancelled;
            _logService.LogWarning($"Task {task.Id} status transition -> Cancelled.");

            return new AgentResult
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

            return new AgentResult
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
        }
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
}
