namespace AIBridge.Models;

public enum TaskPlanStatus
{
    NotStarted,
    Ready,
    Running,
    Reviewing,
    Passed,
    Failed,
    Blocked,
    Paused,
    Cancelled
}
