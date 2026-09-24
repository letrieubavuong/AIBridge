using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class PhaseProgress
{
    public string PhaseId { get; set; } = string.Empty;
    public int PhaseNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int FailedTasks { get; set; }
    public string CurrentTaskId { get; set; } = string.Empty;
    public PhaseStatus Status { get; set; } = PhaseStatus.NotStarted;
    public List<TaskProgress> Tasks { get; set; } = new();

    public double PercentComplete
    {
        get
        {
            if (Status == PhaseStatus.Completed) return 100.0;
            if (TotalTasks <= 0) return 0.0;
            return Math.Min(100.0, Math.Round((double)CompletedTasks / TotalTasks * 100.0, 1));
        }
    }
}
