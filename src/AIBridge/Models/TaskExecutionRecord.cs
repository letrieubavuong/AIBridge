using System;

namespace AIBridge.Models;

public class TaskExecutionRecord
{
    public string ExecutionId { get; set; } = string.Empty;
    public string PromptId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public int PlanVersion { get; set; } = 1;
    public string AgentId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int AttemptNumber { get; set; } = 1;

    public AgentResult? Result { get; set; }
    public CodingAgentExecutionResult? AgentExecutionResult { get; set; }
    public GitEvidence? GitEvidence { get; set; }
    public AgentTask? Task { get; set; }
}
