using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class PlanningRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectName { get; set; } = string.Empty;
    public string Idea { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public List<string> Requirements { get; set; } = new();
    public List<string> Constraints { get; set; } = new();
    public List<string> Preferences { get; set; } = new();
    public string ExistingContext { get; set; } = string.Empty;
    public int? RequestedPhaseCount { get; set; }
    public string RequestedDetailLevel { get; set; } = "Standard";
    public AutomationMode AutomationMode { get; set; } = AutomationMode.Manual;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}
