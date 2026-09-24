using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class TaskPlan
{
    public string TaskId { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public int TaskNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TaskPlanStatus Status { get; set; } = TaskPlanStatus.NotStarted;
    public List<string> Dependencies { get; set; } = new();
    public List<string> AcceptanceCriteria { get; set; } = new();
    public List<string> Constraints { get; set; } = new();
    public string EstimatedComplexity { get; set; } = "Medium";
    public bool RequiresHumanApproval { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
