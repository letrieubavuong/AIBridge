using AIBridge.Models;

namespace AIBridge.Services;

public class TaskService : ITaskService
{
    private readonly ILogService _logService;
    private AgentTask? _currentTask;
    private CancellationTokenSource? _currentCts;

    public event Action<AgentTask?>? CurrentTaskChanged;

    public AgentTask? CurrentTask
    {
        get => _currentTask;
        private set
        {
            _currentTask = value;
            CurrentTaskChanged?.Invoke(_currentTask);
        }
    }

    public TaskService(ILogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public AgentTask CreateTask(string prompt, string workspacePath)
    {
        var task = new AgentTask
        {
            Prompt = prompt,
            WorkspacePath = workspacePath,
            Status = AgentTaskStatus.Pending,
            CreatedAt = DateTime.Now
        };
        _logService.LogInfo($"New agent task created with ID: {task.Id}");
        return task;
    }

    public async Task<AgentResult> SubmitTaskAsync(AgentTask task, ICodingAgentRunner runner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(runner);

        CurrentTask = task;
        task.Status = AgentTaskStatus.Running;
        task.StartedAt = DateTime.Now;
        _logService.LogInfo($"Task {task.Id} started with runner '{runner.Name}'.");

        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var result = await runner.ExecuteAsync(task, _currentCts.Token);
            
            task.CompletedAt = DateTime.Now;
            if (result.Success)
            {
                task.Status = AgentTaskStatus.Success;
                _logService.LogInfo($"Task {task.Id} completed successfully.");
            }
            else
            {
                task.Status = AgentTaskStatus.Failed;
                _logService.LogWarning($"Task {task.Id} failed: {result.ErrorMessage}");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            task.CompletedAt = DateTime.Now;
            task.Status = AgentTaskStatus.Cancelled;
            _logService.LogWarning($"Task {task.Id} was cancelled.");

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
