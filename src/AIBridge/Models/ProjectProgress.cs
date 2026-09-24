using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class ProjectProgress
{
    public string ProjectId { get; set; } = "AIBridge";
    public int TotalPhases { get; set; } = 12;
    public int CompletedPhases { get; set; } = 4; // Phases 01-04 completed
    public int CurrentPhaseNumber { get; set; } = 5;
    public string Status { get; set; } = "Running";
    public List<PhaseProgress> Phases { get; set; } = new();

    public double PercentComplete
    {
        get
        {
            if (TotalPhases <= 0) return 0.0;
            double completedWeight = CompletedPhases * 1.0;
            // Add fractional progress of current phase if available
            var currentPhase = Phases.Find(p => p.PhaseNumber == CurrentPhaseNumber);
            if (currentPhase != null && currentPhase.Status != PhaseStatus.Completed)
            {
                completedWeight += currentPhase.PercentComplete / 100.0;
            }
            return Math.Min(100.0, Math.Round((completedWeight / TotalPhases) * 100.0, 1));
        }
    }
}
