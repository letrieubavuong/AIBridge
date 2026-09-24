using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public class MockBrainProvider : IAIBrainProvider
{
    public BrainProviderDescriptor Descriptor { get; } = new()
    {
        Id = "mock",
        DisplayName = "Mock Brain (Development / Test)",
        RequiresAuthentication = false,
        IsConfigured = true,
        IsAvailable = true,
        SupportsPlanning = true,
        SupportsReview = true
    };

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public async Task<BrainResponse> AnalyzeAsync(BrainRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        await Task.Delay(50, cancellationToken); // Simulate minor processing latency
        sw.Stop();

        var response = new BrainResponse
        {
            RequestId = request.RequestId,
            ProviderId = Descriptor.Id,
            Model = "Development-Mock-v1",
            DurationMs = sw.ElapsedMilliseconds,
            Confidence = 1.0
        };

        switch (request.RequestType)
        {
            case BrainRequestType.ReviewResult:
                if (request.AgentResult != null && request.AgentResult.Success)
                {
                    response.Decision = BrainDecision.PASS;
                    response.Reason = "Agent execution completed successfully with exit code 0.";
                    response.Summary = "PASS: Task output and evidence verified successfully.";
                }
                else
                {
                    response.Decision = BrainDecision.RETRY;
                    response.Reason = request.AgentResult?.ErrorMessage ?? "Agent execution failed or exited with non-zero exit code.";
                    response.Summary = "RETRY: Execution failed; retry attempt requested with corrected context.";
                    response.NextTask = new NextTaskInfo
                    {
                        Title = "Retry Failed Task",
                        Prompt = $"Fix previous error: {request.AgentResult?.ErrorMessage ?? "Unknown failure"}"
                    };
                }
                break;

            case BrainRequestType.Plan:
                response.Decision = BrainDecision.PASS;
                response.Reason = "Mock Brain generated deterministic project plan.";
                response.Summary = "PLAN: Project plan generated successfully.";
                response.RawOutput = GenerateMockProjectPlanJson(request);
                break;

            case BrainRequestType.GenerateTask:
                response.Decision = BrainDecision.NEXT_TASK;
                response.Reason = "Task generation plan produced next actionable step.";
                response.Summary = "NEXT_TASK: Generated next task execution step.";
                response.NextTask = new NextTaskInfo
                {
                    Title = "Execute Planned Task",
                    Prompt = string.IsNullOrWhiteSpace(request.TaskPrompt) ? "Execute workspace task" : request.TaskPrompt
                };
                break;

            case BrainRequestType.DiagnoseFailure:
                response.Decision = BrainDecision.BLOCKED;
                response.Reason = "Diagnosed unrecoverable process failure.";
                response.Summary = "BLOCKED: Execution halted due to fatal error.";
                response.RequiresHumanApproval = true;
                response.HumanGateReason = "Fatal task failure requires human review.";
                break;

            case BrainRequestType.ContinuePhase:
                response.Decision = BrainDecision.NEXT_PHASE;
                response.Reason = "All tasks in current phase verified complete.";
                response.Summary = "NEXT_PHASE: Transition to next milestone phase allowed.";
                response.RequiresHumanApproval = true;
                response.HumanGateReason = "Phase transition requires human gate approval.";
                break;

            default:
                response.Decision = BrainDecision.PASS;
                response.Reason = "Mock Brain default analysis completed successfully.";
                response.Summary = "PASS: Analysis complete.";
                break;
        }

        return response;
    }

    private static string GenerateMockProjectPlanJson(BrainRequest request)
    {
        string projName = string.IsNullOrWhiteSpace(request.ProjectId) || request.ProjectId == "AIBridge"
            ? "Mock Development Application"
            : request.ProjectId;

        string goal = string.IsNullOrWhiteSpace(request.TaskPrompt)
            ? "Build application feature set."
            : request.TaskPrompt;

        var mockPlan = new ProjectPlan
        {
            ProjectId = "project-" + Guid.NewGuid().ToString("N")[..8],
            Name = projName,
            Description = "Deterministic development plan produced by Mock Brain for testing and development.",
            Goal = goal,
            Status = ProjectPlanStatus.Draft,
            Version = 1,
            Phases = new List<PhasePlan>
            {
                new PhasePlan
                {
                    PhaseId = "phase-01",
                    PhaseNumber = 1,
                    Name = "Phase 1 — Foundation",
                    Objective = "Setup core application architecture and repository structure.",
                    Description = "Foundation setup phase.",
                    Status = PhaseStatus.NotStarted,
                    Weight = 1.0,
                    Tasks = new List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = "task-01-01",
                            PhaseId = "phase-01",
                            TaskNumber = 1,
                            Title = "Task 1.1 — Solution Architecture & Setup",
                            Objective = "Initialize solution layout, project dependencies, and base models.",
                            Description = "Configure base project and solution.",
                            Status = TaskPlanStatus.NotStarted,
                            EstimatedComplexity = "Low",
                            MaxRetries = 3
                        },
                        new TaskPlan
                        {
                            TaskId = "task-01-02",
                            PhaseId = "phase-01",
                            TaskNumber = 2,
                            Title = "Task 1.2 — Core Domain Models",
                            Objective = "Define strongly-typed domain entities and interfaces.",
                            Description = "Implement domain objects.",
                            Status = TaskPlanStatus.NotStarted,
                            EstimatedComplexity = "Medium",
                            MaxRetries = 3,
                            Dependencies = new List<string> { "task-01-01" }
                        }
                    }
                },
                new PhasePlan
                {
                    PhaseId = "phase-02",
                    PhaseNumber = 2,
                    Name = "Phase 2 — Core Implementation",
                    Objective = "Implement business services and persistence layers.",
                    Description = "Core features development.",
                    Status = PhaseStatus.NotStarted,
                    Weight = 1.0,
                    Dependencies = new List<string> { "phase-01" },
                    Tasks = new List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = "task-02-01",
                            PhaseId = "phase-02",
                            TaskNumber = 1,
                            Title = "Task 2.1 — Persistence & Storage Layer",
                            Objective = "Build local data persistence and repository services.",
                            Description = "Database or file store implementation.",
                            Status = TaskPlanStatus.NotStarted,
                            EstimatedComplexity = "Medium",
                            MaxRetries = 3,
                            Dependencies = new List<string> { "task-01-02" }
                        },
                        new TaskPlan
                        {
                            TaskId = "task-02-02",
                            PhaseId = "phase-02",
                            TaskNumber = 2,
                            Title = "Task 2.2 — Business Workflows & APIs",
                            Objective = "Implement application services and API controller logic.",
                            Description = "Business service logic.",
                            Status = TaskPlanStatus.NotStarted,
                            EstimatedComplexity = "High",
                            MaxRetries = 3,
                            Dependencies = new List<string> { "task-02-01" }
                        }
                    }
                },
                new PhasePlan
                {
                    PhaseId = "phase-03",
                    PhaseNumber = 3,
                    Name = "Phase 3 — Verification & Delivery",
                    Objective = "Verify complete application functionality via automated testing.",
                    Description = "Verification and final validation.",
                    Status = PhaseStatus.NotStarted,
                    Weight = 1.0,
                    Dependencies = new List<string> { "phase-02" },
                    Tasks = new List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = "task-03-01",
                            PhaseId = "phase-03",
                            TaskNumber = 1,
                            Title = "Task 3.1 — Automated Verification & Delivery",
                            Objective = "Execute automated unit and integration verification tests.",
                            Description = "Run full suite of tests.",
                            Status = TaskPlanStatus.NotStarted,
                            EstimatedComplexity = "Medium",
                            MaxRetries = 3,
                            Dependencies = new List<string> { "task-02-02" }
                        }
                    }
                }
            }
        };

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        return System.Text.Json.JsonSerializer.Serialize(mockPlan, options);
    }
}
