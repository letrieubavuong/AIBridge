using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class PhasePlan
{
    public string PhaseId { get; set; } = string.Empty;
    public int PhaseNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PhaseStatus Status { get; set; } = PhaseStatus.NotStarted;
    public List<TaskPlan> Tasks { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public List<string> AcceptanceCriteria { get; set; } = new();
    public bool RequiresHumanApproval { get; set; }
    public double Weight { get; set; } = 1.0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
