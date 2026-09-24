using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IProjectPlanStore
{
    Task SavePlanAsync(ProjectPlan plan, CancellationToken cancellationToken = default);
    Task<ProjectPlan?> LoadPlanAsync(string projectId, CancellationToken cancellationToken = default);
    Task<ProjectPlan?> LoadPlanVersionAsync(string projectId, int version, CancellationToken cancellationToken = default);
    Task<List<PlanVersion>> GetVersionsAsync(string projectId, CancellationToken cancellationToken = default);
    Task<List<ProjectPlan>> GetProjectsAsync(CancellationToken cancellationToken = default);
}
