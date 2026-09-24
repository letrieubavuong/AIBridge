using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public class PlanningService : IPlanningService
{
    private readonly IAIBrainService _brainService;
    private readonly IPlanValidator _planValidator;
    private readonly IProjectPlanStore _planStore;
    private readonly SemaphoreSlim _planningSemaphore = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    public PlanningService(
        IAIBrainService brainService,
        IPlanValidator planValidator,
        IProjectPlanStore planStore)
    {
        _brainService = brainService ?? throw new ArgumentNullException(nameof(brainService));
        _planValidator = planValidator ?? throw new ArgumentNullException(nameof(planValidator));
        _planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task<PlanningResult> GeneratePlanAsync(PlanningRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return new PlanningResult
            {
                Success = false,
                State = PlanningState.Failed,
                ErrorCode = PlanningErrorCode.InvalidPlan,
                ErrorMessage = "Planning request cannot be null."
            };
        }

        if (string.IsNullOrWhiteSpace(request.Idea) && string.IsNullOrWhiteSpace(request.Goal))
        {
            return new PlanningResult
            {
                Success = false,
                State = PlanningState.Failed,
                ErrorCode = PlanningErrorCode.InvalidPlan,
                ErrorMessage = "Planning request must specify an Idea or Goal."
            };
        }

        await _planningSemaphore.WaitAsync(cancellationToken);
        try
        {
            // 1. Build BrainRequest with BrainRequestType.Plan
            string taskPrompt = string.IsNullOrWhiteSpace(request.Idea) ? request.Goal : request.Idea;
            if (!string.IsNullOrWhiteSpace(request.Goal) && request.Goal != taskPrompt)
            {
                taskPrompt += $" (Goal: {request.Goal})";
            }

            string projName = string.IsNullOrWhiteSpace(request.ProjectName) ? "New AI Project" : request.ProjectName;

            var brainRequest = new BrainRequest
            {
                RequestId = request.RequestId,
                ProjectId = projName,
                RequestType = BrainRequestType.Plan,
                TaskPrompt = taskPrompt,
                ProjectContext = $"Project Name: {projName}\nDetail Level: {request.RequestedDetailLevel}",
                Constraints = request.Constraints
                    .Select((c, idx) => new { Key = $"Constraint_{idx + 1:D3}", Value = c })
                    .ToDictionary(x => x.Key, x => x.Value)
            };

            // 2. Invoke IAIBrainService
            var brainResponse = await _brainService.AnalyzeAsync(brainRequest, cancellationToken);

            if (brainResponse == null || brainResponse.Decision == BrainDecision.BLOCKED && !string.IsNullOrEmpty(brainResponse.ErrorCode))
            {
                return new PlanningResult
                {
                    Success = false,
                    State = brainResponse?.ErrorCode == "BRAIN_TIMEOUT" ? PlanningState.TimedOut : PlanningState.Failed,
                    ErrorCode = brainResponse?.ErrorCode ?? PlanningErrorCode.BrainError,
                    ErrorMessage = brainResponse?.ErrorMessage ?? "Brain failed to return planning analysis."
                };
            }

            // 3. Extract & Parse Structured Output
            string? rawOutput = brainResponse.RawOutput ?? brainResponse.Summary;
            ProjectPlan? parsedPlan = ParseProjectPlanFromRawText(rawOutput);

            if (parsedPlan == null)
            {
                return new PlanningResult
                {
                    Success = false,
                    State = PlanningState.Failed,
                    ErrorCode = PlanningErrorCode.InvalidPlan,
                    ErrorMessage = "Failed to parse structured ProjectPlan from Brain response."
                };
            }

            // 4. Normalize Plan Structure
            NormalizePlan(parsedPlan, request);

            // 5. Validate Plan
            var validation = _planValidator.Validate(parsedPlan);
            if (!validation.IsValid)
            {
                return new PlanningResult
                {
                    Success = false,
                    State = PlanningState.Failed,
                    ProjectPlan = parsedPlan,
                    ValidationResult = validation,
                    ErrorCode = PlanningErrorCode.InvalidPlan,
                    ErrorMessage = $"Plan validation failed with {validation.Errors.Count} error(s)."
                };
            }

            // Initial AI generated plan requires explicit Human Approval in ALL automation modes (Phase 06 Human Gate)
            parsedPlan.Status = ProjectPlanStatus.AwaitingApproval;

            // 6. Persist Plan
            await _planStore.SavePlanAsync(parsedPlan, cancellationToken);

            return new PlanningResult
            {
                Success = true,
                State = PlanningState.AwaitingApproval,
                ProjectPlan = parsedPlan,
                ValidationResult = validation
            };
        }
        catch (OperationCanceledException)
        {
            return new PlanningResult
            {
                Success = false,
                State = PlanningState.Cancelled,
                ErrorCode = PlanningErrorCode.PlanningCancelled,
                ErrorMessage = "Planning operation was cancelled."
            };
        }
        catch (Exception ex)
        {
            return new PlanningResult
            {
                Success = false,
                State = PlanningState.Failed,
                ErrorCode = PlanningErrorCode.PersistenceError,
                ErrorMessage = $"Planning failed: {ex.Message}"
            };
        }
        finally
        {
            _planningSemaphore.Release();
        }
    }

    public async Task<PlanningResult> RevisePlanAsync(
        string projectId,
        string revisionPrompt,
        bool allowProtectedHistoryRevision = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return new PlanningResult
            {
                Success = false,
                State = PlanningState.Failed,
                ErrorCode = PlanningErrorCode.PlanNotFound,
                ErrorMessage = "ProjectId cannot be empty."
            };
        }

        await _planningSemaphore.WaitAsync(cancellationToken);
        try
        {
            var currentPlan = await _planStore.LoadPlanAsync(projectId, cancellationToken);
            if (currentPlan == null)
            {
                return new PlanningResult
                {
                    Success = false,
                    State = PlanningState.Failed,
                    ErrorCode = PlanningErrorCode.PlanNotFound,
                    ErrorMessage = $"Project plan with ID '{projectId}' was not found."
                };
            }

            // Enforce completed-work protection: Active plans with completed phases or passed tasks require human approval before restructuring history
            bool hasCompletedWork = currentPlan.Phases.Any(
                p => p.Status == PhaseStatus.Completed || p.Tasks.Any(t => t.Status == TaskPlanStatus.Passed));

            if (currentPlan.Status == ProjectPlanStatus.Active && hasCompletedWork && !allowProtectedHistoryRevision)
            {
                return new PlanningResult
                {
                    Success = false,
                    State = PlanningState.AwaitingApproval,
                    ProjectPlan = currentPlan,
                    ErrorCode = PlanningErrorCode.ApprovalRequired,
                    ErrorMessage = "The active plan contains completed work. Human approval is required before restructuring completed execution history."
                };
            }

            string existingPlanJson = JsonSerializer.Serialize(currentPlan, _jsonOptions);

            var brainRequest = new BrainRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                ProjectId = currentPlan.ProjectId,
                RequestType = BrainRequestType.Plan,
                TaskPrompt = $"Revise existing project plan '{currentPlan.Name}'. User change request: {revisionPrompt}",
                ProjectContext = $"Existing Plan Version: v{currentPlan.Version}\nExisting Plan Content:\n{existingPlanJson}"
            };

            var brainResponse = await _brainService.AnalyzeAsync(brainRequest, cancellationToken);
            if (brainResponse == null || string.IsNullOrWhiteSpace(brainResponse.RawOutput ?? brainResponse.Summary))
            {
                return new PlanningResult
                {
                    Success = false,
                    State = PlanningState.Failed,
                    ErrorCode = PlanningErrorCode.BrainError,
                    ErrorMessage = "Brain failed to return revised project plan."
                };
            }

            ProjectPlan? revisedPlan = ParseProjectPlanFromRawText(brainResponse.RawOutput ?? brainResponse.Summary);
            if (revisedPlan == null)
            {
                return new PlanningResult
                {
                    Success = false,
                    State = PlanningState.Failed,
                    ErrorCode = PlanningErrorCode.InvalidPlan,
                    ErrorMessage = "Failed to parse revised plan from Brain output."
                };
            }

            // Increment version & retain ProjectId & Name
            revisedPlan.ProjectId = currentPlan.ProjectId;
            if (string.IsNullOrWhiteSpace(revisedPlan.Name)) revisedPlan.Name = currentPlan.Name;
            revisedPlan.Version = currentPlan.Version + 1;
            revisedPlan.Metadata["VersionReason"] = string.IsNullOrWhiteSpace(revisionPrompt) ? $"Revised Version {revisedPlan.Version}" : revisionPrompt;
            revisedPlan.Metadata["VersionSource"] = "AI_REVISION";

            NormalizePlan(revisedPlan, null);

            var validation = _planValidator.Validate(revisedPlan);
            if (!validation.IsValid)
            {
                return new PlanningResult
                {
                    Success = false,
                    State = PlanningState.Failed,
                    ProjectPlan = revisedPlan,
                    ValidationResult = validation,
                    ErrorCode = PlanningErrorCode.InvalidPlan,
                    ErrorMessage = "Revised plan validation failed."
                };
            }

            revisedPlan.Status = ProjectPlanStatus.AwaitingApproval;

            // Calculate change summary
            var changeSummary = CalculateChangeSummary(currentPlan, revisedPlan);

            await _planStore.SavePlanAsync(revisedPlan, cancellationToken);

            return new PlanningResult
            {
                Success = true,
                State = PlanningState.AwaitingApproval,
                ProjectPlan = revisedPlan,
                ValidationResult = validation,
                ChangeSummary = changeSummary
            };
        }
        finally
        {
            _planningSemaphore.Release();
        }
    }

    public async Task<PlanningResult> EditPlanAsync(ProjectPlan modifiedPlan, CancellationToken cancellationToken = default)
    {
        if (modifiedPlan == null || string.IsNullOrWhiteSpace(modifiedPlan.ProjectId))
        {
            return new PlanningResult
            {
                Success = false,
                State = PlanningState.Failed,
                ErrorCode = PlanningErrorCode.InvalidPlan,
                ErrorMessage = "Invalid modified plan or missing ProjectId."
            };
        }

        await _planningSemaphore.WaitAsync(cancellationToken);
        try
        {
            var existingPlan = await _planStore.LoadPlanAsync(modifiedPlan.ProjectId, cancellationToken);
            
            // Normalize & Validate
            NormalizePlan(modifiedPlan, null);
            var validation = _planValidator.Validate(modifiedPlan);
            if (!validation.IsValid)
            {
                return new PlanningResult
                {
                    Success = false,
                    State = PlanningState.Failed,
                    ProjectPlan = modifiedPlan,
                    ValidationResult = validation,
                    ErrorCode = PlanningErrorCode.InvalidPlan,
                    ErrorMessage = "Edited plan failed validation."
                };
            }

            if (existingPlan != null)
            {
                modifiedPlan.Version = existingPlan.Version + 1;
                modifiedPlan.Metadata["VersionReason"] = "User edited plan details directly";
                modifiedPlan.Metadata["VersionSource"] = "USER_EDIT";
            }

            modifiedPlan.Status = ProjectPlanStatus.AwaitingApproval;

            PlanChangeSummary? changeSummary = existingPlan != null ? CalculateChangeSummary(existingPlan, modifiedPlan) : null;

            await _planStore.SavePlanAsync(modifiedPlan, cancellationToken);

            return new PlanningResult
            {
                Success = true,
                State = PlanningState.AwaitingApproval,
                ProjectPlan = modifiedPlan,
                ValidationResult = validation,
                ChangeSummary = changeSummary
            };
        }
        finally
        {
            _planningSemaphore.Release();
        }
    }

    public async Task<PlanningResult> ApprovePlanAsync(string projectId, string? approvalReason = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return new PlanningResult
            {
                Success = false,
                State = PlanningState.Failed,
                ErrorCode = PlanningErrorCode.PlanNotFound,
                ErrorMessage = "ProjectId cannot be empty."
            };
        }

        await _planningSemaphore.WaitAsync(cancellationToken);
        try
        {
            var plan = await _planStore.LoadPlanAsync(projectId, cancellationToken);
            if (plan == null)
            {
                return new PlanningResult
                {
                    Success = false,
                    State = PlanningState.Failed,
                    ErrorCode = PlanningErrorCode.PlanNotFound,
                    ErrorMessage = $"Project plan '{projectId}' not found."
                };
            }

            if (plan.Status == ProjectPlanStatus.Approved)
            {
                return new PlanningResult
                {
                    Success = true,
                    State = PlanningState.Approved,
                    ProjectPlan = plan,
                    ErrorCode = PlanningErrorCode.PlanAlreadyApproved,
                    ErrorMessage = "Plan is already approved."
                };
            }

            var validation = _planValidator.Validate(plan);
            if (!validation.IsValid)
            {
                return new PlanningResult
                {
                    Success = false,
                    State = PlanningState.Failed,
                    ProjectPlan = plan,
                    ValidationResult = validation,
                    ErrorCode = PlanningErrorCode.InvalidPlan,
                    ErrorMessage = "Cannot approve an invalid project plan."
                };
            }

            // Explicit Human Gate Approval
            plan.Status = ProjectPlanStatus.Approved;
            plan.ApprovedAt = DateTime.UtcNow;
            plan.ApprovalStatus = "Approved";
            plan.ApprovalReason = string.IsNullOrWhiteSpace(approvalReason) ? "Approved by user" : approvalReason;
            plan.UpdatedAt = DateTime.UtcNow;

            await _planStore.SavePlanAsync(plan, cancellationToken);

            // Phase 06 rule: Approval MUST NOT start Antigravity or execute tasks.
            return new PlanningResult
            {
                Success = true,
                State = PlanningState.Approved,
                ProjectPlan = plan,
                ValidationResult = validation
            };
        }
        finally
        {
            _planningSemaphore.Release();
        }
    }

    public Task<ProjectPlan?> GetPlanAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return _planStore.LoadPlanAsync(projectId, cancellationToken);
    }

    public Task<ProjectPlan?> GetPlanVersionAsync(string projectId, int version, CancellationToken cancellationToken = default)
    {
        return _planStore.LoadPlanVersionAsync(projectId, version, cancellationToken);
    }

    public Task<List<PlanVersion>> GetVersionsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return _planStore.GetVersionsAsync(projectId, cancellationToken);
    }

    public Task<List<ProjectPlan>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        return _planStore.GetProjectsAsync(cancellationToken);
    }

    public ProjectProgress CalculateProjectProgress(ProjectPlan plan)
    {
        if (plan == null || plan.Phases == null || plan.Phases.Count == 0)
        {
            return new ProjectProgress
            {
                ProjectId = plan?.ProjectId ?? string.Empty,
                TotalPhases = 0,
                CompletedPhases = 0,
                CurrentPhaseNumber = 0,
                Status = plan?.Status.ToString() ?? "Draft"
            };
        }

        var phaseProgressList = plan.Phases.Select(CalculatePhaseProgress).ToList();
        int totalPhases = plan.Phases.Count;
        int completedPhases = phaseProgressList.Count(p => p.Status == PhaseStatus.Completed);

        var currentPhase = phaseProgressList.FirstOrDefault(p => p.Status == PhaseStatus.Running || p.Status == PhaseStatus.Ready || p.Status == PhaseStatus.NotStarted)
                           ?? phaseProgressList.LastOrDefault();

        int currentPhaseNum = currentPhase?.PhaseNumber ?? 1;

        // Weighted percentage calculation
        double totalWeight = plan.Phases.Sum(p => p.Weight > 0 ? p.Weight : 1.0);
        double completedWeight = 0.0;

        foreach (var phase in plan.Phases)
        {
            double pWeight = phase.Weight > 0 ? phase.Weight : 1.0;
            var pProg = phaseProgressList.FirstOrDefault(pp => string.Equals(pp.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase));
            if (pProg != null)
            {
                if (pProg.Status == PhaseStatus.Completed)
                {
                    completedWeight += pWeight;
                }
                else if (pProg.Status == PhaseStatus.Running || pProg.Status == PhaseStatus.Reviewing)
                {
                    completedWeight += (pProg.PercentComplete / 100.0) * pWeight;
                }
            }
        }

        double percentComplete = totalWeight > 0 ? Math.Min(100.0, Math.Round((completedWeight / totalWeight) * 100.0, 1)) : 0.0;

        return new ProjectProgress
        {
            ProjectId = plan.ProjectId,
            TotalPhases = totalPhases,
            CompletedPhases = completedPhases,
            CurrentPhaseNumber = currentPhaseNum,
            Status = plan.Status.ToString(),
            Phases = phaseProgressList
        };
    }

    public PhaseProgress CalculatePhaseProgress(PhasePlan phase)
    {
        if (phase == null || phase.Tasks == null || phase.Tasks.Count == 0)
        {
            return new PhaseProgress
            {
                PhaseId = phase?.PhaseId ?? string.Empty,
                PhaseNumber = phase?.PhaseNumber ?? 1,
                Name = phase?.Name ?? string.Empty,
                TotalTasks = 0,
                CompletedTasks = 0,
                FailedTasks = 0,
                Status = phase?.Status ?? PhaseStatus.NotStarted,
                Tasks = new List<TaskProgress>()
            };
        }

        int totalLogicalTasks = phase.Tasks.Count;
        int passedTasks = phase.Tasks.Count(t => t.Status == TaskPlanStatus.Passed);
        int failedTasks = phase.Tasks.Count(t => t.Status == TaskPlanStatus.Failed);

        var taskProgressList = phase.Tasks.Select(t => new TaskProgress
        {
            TaskId = t.TaskId,
            Title = t.Title,
            IsCompleted = t.Status == TaskPlanStatus.Passed,
            IsFailed = t.Status == TaskPlanStatus.Failed,
            RetryCount = t.RetryCount
        }).ToList();

        var currentTask = phase.Tasks.FirstOrDefault(t => t.Status == TaskPlanStatus.Running || t.Status == TaskPlanStatus.Ready)
                          ?? phase.Tasks.FirstOrDefault(t => t.Status == TaskPlanStatus.NotStarted);

        PhaseStatus derivedStatus = phase.Status;
        if (passedTasks == totalLogicalTasks && totalLogicalTasks > 0)
        {
            derivedStatus = PhaseStatus.Completed;
        }

        return new PhaseProgress
        {
            PhaseId = phase.PhaseId,
            PhaseNumber = phase.PhaseNumber,
            Name = phase.Name,
            TotalTasks = totalLogicalTasks,
            CompletedTasks = passedTasks,
            FailedTasks = failedTasks,
            CurrentTaskId = currentTask?.TaskId ?? string.Empty,
            Status = derivedStatus,
            Tasks = taskProgressList
        };
    }

    private static void NormalizePlan(ProjectPlan plan, PlanningRequest? request)
    {
        if (string.IsNullOrWhiteSpace(plan.ProjectId))
        {
            plan.ProjectId = "project-" + Guid.NewGuid().ToString("N")[..8];
        }

        if (request != null)
        {
            if (string.IsNullOrWhiteSpace(plan.Name)) plan.Name = request.ProjectName;
            if (string.IsNullOrWhiteSpace(plan.Goal)) plan.Goal = request.Goal;
            if (string.IsNullOrWhiteSpace(plan.Description)) plan.Description = request.Idea;
        }

        if (string.IsNullOrWhiteSpace(plan.Name)) plan.Name = "AI Project Plan";
        if (string.IsNullOrWhiteSpace(plan.Goal)) plan.Goal = "Execute development project requirements.";

        if (plan.Phases == null) plan.Phases = new List<PhasePlan>();

        for (int i = 0; i < plan.Phases.Count; i++)
        {
            var phase = plan.Phases[i];
            phase.PhaseNumber = i + 1;
            if (string.IsNullOrWhiteSpace(phase.PhaseId))
            {
                phase.PhaseId = $"phase-{phase.PhaseNumber:D2}";
            }
            if (string.IsNullOrWhiteSpace(phase.Name))
            {
                phase.Name = $"Phase {phase.PhaseNumber:D2}";
            }
            if (string.IsNullOrWhiteSpace(phase.Objective))
            {
                phase.Objective = $"Execute Phase {phase.PhaseNumber:D2} deliverables.";
            }
            if (phase.Weight <= 0)
            {
                phase.Weight = 1.0;
            }

            if (phase.Tasks == null) phase.Tasks = new List<TaskPlan>();

            for (int j = 0; j < phase.Tasks.Count; j++)
            {
                var task = phase.Tasks[j];
                task.TaskNumber = j + 1;
                task.PhaseId = phase.PhaseId;

                if (string.IsNullOrWhiteSpace(task.TaskId))
                {
                    task.TaskId = $"task-{phase.PhaseNumber:D2}-{task.TaskNumber:D2}";
                }
                if (string.IsNullOrWhiteSpace(task.Title))
                {
                    task.Title = $"Task {phase.PhaseNumber}.{task.TaskNumber}";
                }
                if (string.IsNullOrWhiteSpace(task.Objective))
                {
                    task.Objective = task.Title;
                }
                if (task.MaxRetries < 0)
                {
                    task.MaxRetries = 3;
                }

                // Phase 06 generated tasks MUST initially be NotStarted
                if (task.Status == TaskPlanStatus.Running)
                {
                    task.Status = TaskPlanStatus.NotStarted;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(plan.CurrentPhaseId) && plan.Phases.Count > 0)
        {
            plan.CurrentPhaseId = plan.Phases[0].PhaseId;
        }
    }

    private ProjectPlan? ParseProjectPlanFromRawText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string json = text.Trim();

        // Strip Markdown code fences if present (e.g. ```json ... ```)
        if (json.StartsWith("```"))
        {
            int firstNewline = json.IndexOf('\n');
            if (firstNewline > 0)
            {
                json = json[firstNewline..].Trim();
            }
            if (json.EndsWith("```"))
            {
                json = json[..^3].Trim();
            }
        }

        try
        {
            return JsonSerializer.Deserialize<ProjectPlan>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static PlanChangeSummary CalculateChangeSummary(ProjectPlan oldPlan, ProjectPlan newPlan)
    {
        var summary = new PlanChangeSummary();

        var oldPhaseIds = oldPlan.Phases.Select(p => p.PhaseId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newPhaseIds = newPlan.Phases.Select(p => p.PhaseId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        summary.AddedPhases = newPlan.Phases.Where(p => !oldPhaseIds.Contains(p.PhaseId)).Select(p => p.Name).ToList();
        summary.RemovedPhases = oldPlan.Phases.Where(p => !newPhaseIds.Contains(p.PhaseId)).Select(p => p.Name).ToList();

        var commonPhases = newPlan.Phases.Where(p => oldPhaseIds.Contains(p.PhaseId));
        foreach (var np in commonPhases)
        {
            var op = oldPlan.Phases.First(p => string.Equals(p.PhaseId, np.PhaseId, StringComparison.OrdinalIgnoreCase));
            if (np.Name != op.Name || np.Objective != op.Objective || np.Weight != op.Weight)
            {
                summary.ModifiedPhases.Add(np.Name);
            }
        }

        var oldTasks = oldPlan.Phases.SelectMany(p => p.Tasks).ToDictionary(t => t.TaskId, StringComparer.OrdinalIgnoreCase);
        var newTasks = newPlan.Phases.SelectMany(p => p.Tasks).ToDictionary(t => t.TaskId, StringComparer.OrdinalIgnoreCase);

        summary.AddedTasks = newTasks.Where(kv => !oldTasks.ContainsKey(kv.Key)).Select(kv => kv.Value.Title).ToList();
        summary.RemovedTasks = oldTasks.Where(kv => !newTasks.ContainsKey(kv.Key)).Select(kv => kv.Value.Title).ToList();

        foreach (var (taskId, nt) in newTasks)
        {
            if (oldTasks.TryGetValue(taskId, out var ot))
            {
                if (nt.Title != ot.Title || nt.Objective != ot.Objective || nt.MaxRetries != ot.MaxRetries)
                {
                    summary.ModifiedTasks.Add(nt.Title);
                }

                var oldDeps = ot.Dependencies ?? new List<string>();
                var newDeps = nt.Dependencies ?? new List<string>();
                if (!oldDeps.SequenceEqual(newDeps))
                {
                    summary.DependencyChanges.Add($"Dependencies updated for task '{nt.Title}'");
                }
            }
        }

        return summary;
    }
}
