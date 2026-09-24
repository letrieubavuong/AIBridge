namespace AIBridge.Models;

public class BrainResponse
{
    public string RequestId { get; set; } = string.Empty;
    public BrainDecision Decision { get; set; } = BrainDecision.BLOCKED;
    public string Reason { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public NextTaskInfo? NextTask { get; set; }
    public bool RequiresHumanApproval { get; set; }
    public string? HumanGateReason { get; set; }
    public double? Confidence { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawOutput { get; set; }
}
