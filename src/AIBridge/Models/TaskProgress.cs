namespace AIBridge.Models;

public class TaskProgress
{
    public string TaskId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsFailed { get; set; }
    public int RetryCount { get; set; }
}
