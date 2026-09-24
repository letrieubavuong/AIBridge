using AIBridge.Infrastructure;

namespace AIBridge.Models;

public class AgentTask : ObservableObject
{
    private AgentTaskStatus _status = AgentTaskStatus.Pending;
    private DateTime? _startedAt;
    private DateTime? _completedAt;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Prompt { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public string? ConfiguredAgentPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;


    public AgentTaskStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public DateTime? StartedAt
    {
        get => _startedAt;
        set => SetProperty(ref _startedAt, value);
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set => SetProperty(ref _completedAt, value);
    }
}

