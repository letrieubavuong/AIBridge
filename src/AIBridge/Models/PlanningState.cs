namespace AIBridge.Models;

public enum PlanningState
{
    Idle,
    Planning,
    Validating,
    Draft,
    AwaitingApproval,
    Approved,
    Failed,
    Cancelled,
    TimedOut
}
