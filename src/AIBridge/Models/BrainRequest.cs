using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class BrainRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "AIBridge";
    public string PhaseId { get; set; } = "Phase05";
    public string TaskId { get; set; } = string.Empty;
    public BrainRequestType RequestType { get; set; } = BrainRequestType.General;
    public string ProjectContext { get; set; } = string.Empty;
    public string PhaseContext { get; set; } = string.Empty;
    public string TaskContext { get; set; } = string.Empty;
    public string TaskPrompt { get; set; } = string.Empty;
    public AgentResult? AgentResult { get; set; }
    public GitEvidence? GitEvidence { get; set; }
    public BrainDecision? PreviousBrainDecision { get; set; }
    public int RetryCount { get; set; }
    public Dictionary<string, string> Constraints { get; set; } = new();
    public DateTime RequestedAt { get; set; } = DateTime.Now;
}
