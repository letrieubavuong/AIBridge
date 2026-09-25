using System;

namespace AIBridge.Models;

public class HumanApprovalRecord
{
    public string ApprovalId { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public int PlanVersion { get; set; } = 1;
    public string ApprovalType { get; set; } = "ExecutionApproval";
    public DateTime ApprovedAt { get; set; } = DateTime.Now;
    public string ApprovedBy { get; set; } = "LocalHuman";
    public DateTime? ConsumedAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public string? PromptId { get; set; }
    public string? PromptHash { get; set; }

    public bool IsValid => !IsRevoked && !ConsumedAt.HasValue;
}
