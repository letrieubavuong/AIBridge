using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IPlanningService
{
    Task<PlanningResult> GeneratePlanAsync(PlanningRequest request, CancellationToken cancellationToken = default);
    Task<PlanningResult> RevisePlanAsync(string projectId, string revisionPrompt, CancellationToken cancellationToken = default);
    Task<PlanningResult> EditPlanAsync(ProjectPlan modifiedPlan, CancellationToken cancellationToken = default);
    Task<PlanningResult> ApprovePlanAsync(string projectId, string? approvalReason = null, CancellationToken cancellationToken = default);
    Task<ProjectPlan?> GetPlanAsync(string projectId, CancellationToken cancellationToken = default);
    Task<ProjectPlan?> GetPlanVersionAsync(string projectId, int version, CancellationToken cancellationToken = default);
    Task<List<PlanVersion>> GetVersionsAsync(string projectId, CancellationToken cancellationToken = default);
    Task<List<ProjectPlan>> GetProjectsAsync(CancellationToken cancellationToken = default);
    ProjectProgress CalculateProjectProgress(ProjectPlan plan);
    PhaseProgress CalculatePhaseProgress(PhasePlan phase);
}
