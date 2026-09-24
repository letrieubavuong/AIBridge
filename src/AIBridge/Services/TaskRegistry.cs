using System.Collections.Concurrent;
using AIBridge.Models;

namespace AIBridge.Services;

public class TaskRegistry : ITaskRegistry
{
    private readonly ConcurrentDictionary<string, TaskExecutionRecord> _records = new();
    private readonly ConcurrentQueue<string> _order = new();
    private readonly object _lock = new();
    private const int MaxCapacity = 100;
    private string? _currentTaskId;

    public void RegisterTask(AgentTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_lock)
        {
            _currentTaskId = task.Id;

            var record = new TaskExecutionRecord { Task = task };
            if (_records.TryAdd(task.Id, record))
            {
                _order.Enqueue(task.Id);

                // Trim capacity if exceeds MaxCapacity
                while (_records.Count > MaxCapacity && _order.TryDequeue(out var oldId))
                {
                    _records.TryRemove(oldId, out _);
                }
            }
        }
    }

    public void RecordResult(string taskId, AgentResult result)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        ArgumentNullException.ThrowIfNull(result);

        if (_records.TryGetValue(taskId, out var record))
        {
            lock (_lock)
            {
                record.Result = result;
            }
        }
    }

    public AgentTask? GetTask(string taskId)
    {
        return _records.TryGetValue(taskId, out var record) ? record.Task : null;
    }

    public TaskExecutionRecord? GetRecord(string taskId)
    {
        return _records.TryGetValue(taskId, out var record) ? record : null;
    }

    public TaskExecutionRecord? GetCurrentRecord()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_currentTaskId))
            {
                return null;
            }
            return _records.TryGetValue(_currentTaskId, out var record) ? record : null;
        }
    }

    public IReadOnlyList<TaskExecutionRecord> GetRecentRecords(int limit = 50)
    {
        limit = Math.Clamp(limit, 1, MaxCapacity);
        lock (_lock)
        {
            return _records.Values
                .OrderByDescending(r => r.Task.CreatedAt)
                .Take(limit)
                .ToList();
        }
    }
}
