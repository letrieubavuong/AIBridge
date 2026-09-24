using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class ExecutionPromptPackage
{
    public string PromptId { get; set; } = Guid.NewGuid().ToString("N");
    public string GeneratedBy { get; set; } = "ChatGPTWeb";

    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;

    public string PhaseId { get; set; } = string.Empty;
    public int PhaseNumber { get; set; }
    public string PhaseName { get; set; } = string.Empty;
    public string PhaseObjective { get; set; } = string.Empty;

    public string TaskId { get; set; } = string.Empty;
    public int TaskNumber { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public string TaskObjective { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;
    public List<string> AcceptanceCriteria { get; set; } = new();
    public List<string> Constraints { get; set; } = new();
    public List<string> VerificationInstructions { get; set; } = new();
    public string CommitInstructions { get; set; } = string.Empty;

    public string WorkspaceContext { get; set; } = string.Empty;
    public string RepositoryContext { get; set; } = string.Empty;

    public string GeneratedPrompt { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public int PlanVersion { get; set; } = 1;

    public string PromptHash { get; set; } = string.Empty;
    public bool WasTruncated { get; set; }
    public int ByteSize { get; set; }
}
