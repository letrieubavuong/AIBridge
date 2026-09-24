namespace AIBridge.Models;

public class PlanningResult
{
    public bool Success { get; set; }
    public PlanningState State { get; set; } = PlanningState.Idle;
    public ProjectPlan? ProjectPlan { get; set; }
    public PlanningValidationResult? ValidationResult { get; set; }
    public PlanChangeSummary? ChangeSummary { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
