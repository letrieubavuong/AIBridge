namespace AIBridge.Models;

public static class CodingAgentErrorCode
{
    public const string CodingAgentDisabled = "CODING_AGENT_DISABLED";
    public const string AgentNotFound = "AGENT_NOT_FOUND";
    public const string AgentNotConfigured = "AGENT_NOT_CONFIGURED";
    public const string AgentUnavailable = "AGENT_UNAVAILABLE";
    public const string WorkspaceInvalid = "WORKSPACE_INVALID";
    public const string PromptInvalid = "PROMPT_INVALID";
    public const string TaskNotDispatchable = "TASK_NOT_DISPATCHABLE";
    public const string DependencyNotSatisfied = "DEPENDENCY_NOT_SATISFIED";
    public const string HumanApprovalRequired = "HUMAN_APPROVAL_REQUIRED";
    public const string AgentTimeout = "AGENT_TIMEOUT";
    public const string AgentCancelled = "AGENT_CANCELLED";
    public const string AgentPermissionDenied = "AGENT_PERMISSION_DENIED";
    public const string AgentExecutionFailed = "AGENT_EXECUTION_FAILED";
    public const string AgentOutputTooLarge = "AGENT_OUTPUT_TOO_LARGE";
    public const string PromptStale = "PROMPT_STALE";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
}
