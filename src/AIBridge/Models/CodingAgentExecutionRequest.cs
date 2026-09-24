using System;

namespace AIBridge.Models;

public class CodingAgentExecutionRequest
{
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString("N");
    public string PromptId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
    public string EnvironmentContext { get; set; } = string.Empty;
    public bool CommitRequired { get; set; } = true;
    public string CommitMessage { get; set; } = string.Empty;
    public bool PushRequested { get; set; } = false;
    public DateTime RequestedAt { get; set; } = DateTime.Now;
    public int PlanVersion { get; set; } = 1;
}
