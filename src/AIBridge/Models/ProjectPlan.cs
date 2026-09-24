using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class ProjectPlan
{
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public ProjectPlanStatus Status { get; set; } = ProjectPlanStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; } = 1;
    public string CurrentPhaseId { get; set; } = string.Empty;
    public List<PhasePlan> Phases { get; set; } = new();
    public List<string> Constraints { get; set; } = new();
    public List<string> Assumptions { get; set; } = new();
    public List<string> AcceptanceCriteria { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovalStatus { get; set; }
    public string? ApprovalReason { get; set; }
}
