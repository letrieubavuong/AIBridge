using System;

namespace AIBridge.Models;

public class CodingAgentExecutionResult
{
    public string ExecutionId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = string.Empty;
    public string Stderr { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public long DurationMs { get; set; }
    public bool WasCancelled { get; set; }
    public bool TimedOut { get; set; }
    public bool PermissionDenied { get; set; }
    public string AgentTaskId { get; set; } = string.Empty;
}
