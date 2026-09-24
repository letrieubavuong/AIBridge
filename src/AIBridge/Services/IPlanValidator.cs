using AIBridge.Models;

namespace AIBridge.Services;

public interface IPlanValidator
{
    PlanningValidationResult Validate(ProjectPlan plan);
}
