using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class ExecutionReviewPackage
{
    public string ExecutionId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public int PlanVersion { get; set; } = 1;

    public string TaskTitle { get; set; } = string.Empty;
    public string TaskObjective { get; set; } = string.Empty;
    public List<string> AcceptanceCriteria { get; set; } = new();

    public int AttemptNumber { get; set; } = 1;
    public string AgentId { get; set; } = string.Empty;
    public bool AgentSuccess { get; set; }
    public int ExitCode { get; set; }
    public string OutputSummary { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;

    public GitEvidence? GitEvidence { get; set; }

    public string CommitSha { get; set; } = string.Empty;
    public string CommitMessage { get; set; } = string.Empty;
    public List<string> ChangedFiles { get; set; } = new();
    public string Diff { get; set; } = string.Empty;
    public bool DiffTruncated { get; set; }

    public DateTime ExecutionStartedAt { get; set; }
    public DateTime ExecutionCompletedAt { get; set; }
    public string ReviewStatus { get; set; } = "Pending"; // Always "Pending" after execution; ChatGPT Web will review
}
