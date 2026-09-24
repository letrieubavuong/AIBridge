namespace AIBridge.Models;

public enum BrainRequestType
{
    Plan,
    GenerateTask,
    ReviewResult,
    DiagnoseFailure,
    ContinuePhase,
    General
}

public enum BrainDecision
{
    PASS,
    RETRY,
    BLOCKED,
    NEXT_TASK,
    NEXT_PHASE,
    STOP,
    ASK_HUMAN
}

public enum BrainState
{
    Disabled,
    NotConfigured,
    Ready,
    Thinking,
    Completed,
    Failed,
    Cancelled,
    TimedOut
}

public enum AutomationMode
{
    Manual,
    PhaseAuto,
    FullAuto
}

public enum PhaseStatus
{
    NotStarted,
    Planning,
    Ready,
    Running,
    Reviewing,
    Completed,
    Blocked,
    Failed,
    Paused
}
