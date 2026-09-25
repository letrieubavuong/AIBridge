namespace AIBridge.Models;

public class HumanApprovalRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public int PlanVersion { get; set; } = 1;
    public string? PromptId { get; set; }
    public string? PromptHash { get; set; }
    public string ApprovedBy { get; set; } = "LocalHuman";
}
