using System;
using System.IO;
using System.Threading.Tasks;
using AIBridge.Infrastructure;
using AIBridge.Models;
using AIBridge.Services;
using Xunit;

namespace AIBridge.Tests;

public class PlanningServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileProjectPlanStore _store;
    private readonly MockBrainProvider _mockBrain;
    private readonly AIBrainProviderRegistry _brainRegistry;
    private readonly AIBrainService _brainService;
    private readonly PlanValidator _validator;
    private readonly PlanningService _planningService;

    public PlanningServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AIBridge_PlanService_" + Guid.NewGuid().ToString("N"));
        _store = new FileProjectPlanStore(_testDir);
        _mockBrain = new MockBrainProvider();
        _brainRegistry = new AIBrainProviderRegistry();
        _brainRegistry.RegisterProvider(_mockBrain);

        var logService = new LogService();
        var configService = new ConfigService(logService);

        _brainService = new AIBrainService(logService, configService, _brainRegistry);
        _validator = new PlanValidator();
        _planningService = new PlanningService(_brainService, _validator, _store);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task RealPlanningTest_MockBrain_GeneratesValidAwaitingApprovalPlan()
    {
        var request = new PlanningRequest
        {
            ProjectName = "Book Manager",
            Idea = "Build a small Windows desktop application that manages a list of books. Users can add, edit, delete and search books. Data should persist locally.",
            Goal = "Manage books locally."
        };

        var result = await _planningService.GeneratePlanAsync(request);

        Assert.True(result.Success);
        Assert.Equal(PlanningState.AwaitingApproval, result.State);
        Assert.NotNull(result.ProjectPlan);
        Assert.Equal(ProjectPlanStatus.AwaitingApproval, result.ProjectPlan.Status);
        Assert.True(result.ProjectPlan.Phases.Count > 0);

        // Verify IDs unique and stable
        var phaseIds = new System.Collections.Generic.HashSet<string>();
        var taskIds = new System.Collections.Generic.HashSet<string>();

        foreach (var phase in result.ProjectPlan.Phases)
        {
            Assert.True(phaseIds.Add(phase.PhaseId));
            Assert.True(phase.Tasks.Count > 0);
            foreach (var task in phase.Tasks)
            {
                Assert.True(taskIds.Add(task.TaskId));
                Assert.Equal(TaskPlanStatus.NotStarted, task.Status); // Must be NotStarted
            }
        }

        // Verify persistence reload
        var reloaded = await _planningService.GetPlanAsync(result.ProjectPlan.ProjectId);
        Assert.NotNull(reloaded);
        Assert.Equal(result.ProjectPlan.ProjectId, reloaded.ProjectId);
    }

    [Fact]
    public async Task HumanGateTest_InitialPlanRequiresHumanApproval_DoesNotExecuteTasks()
    {
        var request = new PlanningRequest
        {
            ProjectName = "Human Gate Test App",
            Idea = "Test human gate lifecycle",
            AutomationMode = AutomationMode.FullAuto // Even FullAuto requires initial plan human approval!
        };

        var genResult = await _planningService.GeneratePlanAsync(request);

        Assert.Equal(PlanningState.AwaitingApproval, genResult.State);
        Assert.Equal(ProjectPlanStatus.AwaitingApproval, genResult.ProjectPlan!.Status);

        // Explicit approval
        var approveResult = await _planningService.ApprovePlanAsync(genResult.ProjectPlan.ProjectId, "Explicit human approval");

        Assert.True(approveResult.Success);
        Assert.Equal(PlanningState.Approved, approveResult.State);
        Assert.Equal(ProjectPlanStatus.Approved, approveResult.ProjectPlan!.Status);
        Assert.NotNull(approveResult.ProjectPlan.ApprovedAt);
        Assert.Equal("Explicit human approval", approveResult.ProjectPlan.ApprovalReason);
    }

    [Fact]
    public async Task VersioningTest_RevisingPlan_CreatesV2AndRetainsHistory()
    {
        var request = new PlanningRequest
        {
            ProjectName = "Versioned System",
            Idea = "Initial system specification"
        };

        var v1Result = await _planningService.GeneratePlanAsync(request);
        Assert.Equal(1, v1Result.ProjectPlan!.Version);

        // Revise plan
        var v2Result = await _planningService.RevisePlanAsync(v1Result.ProjectPlan.ProjectId, "Add advanced reporting feature");

        Assert.True(v2Result.Success);
        Assert.Equal(2, v2Result.ProjectPlan!.Version);
        Assert.NotNull(v2Result.ChangeSummary);

        // Verify both v1 and v2 are retrievable
        var loadedV1 = await _planningService.GetPlanVersionAsync(v1Result.ProjectPlan.ProjectId, 1);
        var loadedV2 = await _planningService.GetPlanVersionAsync(v1Result.ProjectPlan.ProjectId, 2);

        Assert.NotNull(loadedV1);
        Assert.NotNull(loadedV2);
        Assert.Equal(1, loadedV1.Version);
        Assert.Equal(2, loadedV2.Version);

        var versions = await _planningService.GetVersionsAsync(v1Result.ProjectPlan.ProjectId);
        Assert.Equal(2, versions.Count);
    }

    // --- TEST 1 — ACTIVE PLAN WITHOUT COMPLETED WORK ---
    [Fact]
    public async Task RevisePlan_ActivePlanWithoutCompletedWork_AllowsRevisionWithoutGate()
    {
        var request = new PlanningRequest { ProjectName = "Active Proj", Idea = "Initial Idea" };
        var genResult = await _planningService.GeneratePlanAsync(request);
        Assert.True(genResult.Success);

        var plan = genResult.ProjectPlan!;
        plan.Status = ProjectPlanStatus.Active;
        await _store.SavePlanAsync(plan);

        int initialBrainCount = _mockBrain.CallCount;

        var reviseResult = await _planningService.RevisePlanAsync(plan.ProjectId, "Minor tweak", allowProtectedHistoryRevision: false);

        Assert.True(reviseResult.Success);
        Assert.True(_mockBrain.CallCount > initialBrainCount);
        Assert.Equal(2, reviseResult.ProjectPlan!.Version);
    }

    // --- TEST 2 — COMPLETED PHASE PROTECTION ---
    [Fact]
    public async Task RevisePlan_CompletedPhaseProtection_BlocksRevisionWithoutApproval()
    {
        var request = new PlanningRequest { ProjectName = "Completed Phase Proj", Idea = "Initial Idea" };
        var genResult = await _planningService.GeneratePlanAsync(request);
        var plan = genResult.ProjectPlan!;
        plan.Status = ProjectPlanStatus.Active;
        plan.Phases[0].Status = PhaseStatus.Completed;
        await _store.SavePlanAsync(plan);

        int initialBrainCount = _mockBrain.CallCount;

        var reviseResult = await _planningService.RevisePlanAsync(plan.ProjectId, "Restructure completed phase", allowProtectedHistoryRevision: false);

        Assert.False(reviseResult.Success);
        Assert.Equal(PlanningErrorCode.ApprovalRequired, reviseResult.ErrorCode);
        Assert.Equal(PlanningState.AwaitingApproval, reviseResult.State);
        Assert.Equal(_mockBrain.CallCount, initialBrainCount); // Brain invoked = NO

        // Version history intact (only v1 exists)
        var versions = await _planningService.GetVersionsAsync(plan.ProjectId);
        Assert.Single(versions);

        // Existing plan unchanged in store
        var storedPlan = await _planningService.GetPlanAsync(plan.ProjectId);
        Assert.Equal(1, storedPlan!.Version);
        Assert.Equal(PhaseStatus.Completed, storedPlan.Phases[0].Status);
    }

    // --- TEST 3 — PASSED TASK PROTECTION ---
    [Fact]
    public async Task RevisePlan_PassedTaskProtection_BlocksRevisionWithoutApproval()
    {
        var request = new PlanningRequest { ProjectName = "Passed Task Proj", Idea = "Initial Idea" };
        var genResult = await _planningService.GeneratePlanAsync(request);
        var plan = genResult.ProjectPlan!;
        plan.Status = ProjectPlanStatus.Active;
        plan.Phases[0].Status = PhaseStatus.Running;
        plan.Phases[0].Tasks[0].Status = TaskPlanStatus.Passed;
        await _store.SavePlanAsync(plan);

        int initialBrainCount = _mockBrain.CallCount;

        var reviseResult = await _planningService.RevisePlanAsync(plan.ProjectId, "Restructure task", allowProtectedHistoryRevision: false);

        Assert.False(reviseResult.Success);
        Assert.Equal(PlanningErrorCode.ApprovalRequired, reviseResult.ErrorCode);
        Assert.Equal(_mockBrain.CallCount, initialBrainCount);

        var storedPlan = await _planningService.GetPlanAsync(plan.ProjectId);
        Assert.Equal(1, storedPlan!.Version);
    }

    // --- TEST 4 — EXPLICIT PROTECTED-HISTORY AUTHORIZATION ---
    [Fact]
    public async Task RevisePlan_ExplicitProtectedHistoryAuthorization_AllowsRevisionAndCreatesV2()
    {
        var request = new PlanningRequest { ProjectName = "Auth Proj", Idea = "Initial Idea" };
        var genResult = await _planningService.GeneratePlanAsync(request);
        var plan = genResult.ProjectPlan!;
        plan.Status = ProjectPlanStatus.Active;
        plan.Phases[0].Status = PhaseStatus.Completed;
        await _store.SavePlanAsync(plan);

        int initialBrainCount = _mockBrain.CallCount;

        var reviseResult = await _planningService.RevisePlanAsync(plan.ProjectId, "Authorized restructuring", allowProtectedHistoryRevision: true);

        Assert.True(reviseResult.Success);
        Assert.True(_mockBrain.CallCount > initialBrainCount); // Brain invoked = YES
        Assert.Equal(2, reviseResult.ProjectPlan!.Version);

        // Verify v1 retained, v2 created
        var loadedV1 = await _planningService.GetPlanVersionAsync(plan.ProjectId, 1);
        var loadedV2 = await _planningService.GetPlanVersionAsync(plan.ProjectId, 2);

        Assert.NotNull(loadedV1);
        Assert.NotNull(loadedV2);
        Assert.Equal(PhaseStatus.Completed, loadedV1.Phases[0].Status);
        Assert.Equal(2, loadedV2.Version);
    }

    // --- TEST 5 — FULLAUTO DOES NOT BYPASS HISTORY GATE ---
    [Fact]
    public async Task RevisePlan_FullAutoMode_DoesNotBypassCompletedHistoryGate()
    {
        var request = new PlanningRequest
        {
            ProjectName = "FullAuto Proj",
            Idea = "Initial Idea",
            AutomationMode = AutomationMode.FullAuto
        };
        var genResult = await _planningService.GeneratePlanAsync(request);
        var plan = genResult.ProjectPlan!;
        plan.Status = ProjectPlanStatus.Active;
        plan.Phases[0].Status = PhaseStatus.Completed;
        await _store.SavePlanAsync(plan);

        var reviseResult = await _planningService.RevisePlanAsync(plan.ProjectId, "Revision under FullAuto", allowProtectedHistoryRevision: false);

        Assert.False(reviseResult.Success);
        Assert.Equal(PlanningErrorCode.ApprovalRequired, reviseResult.ErrorCode);
    }

    [Fact]
    public void CalculateProgress_DerivedFromActualPlanPhasesAndTasks()
    {
        var plan = new ProjectPlan
        {
            ProjectId = "proj-prog",
            Name = "Progress Test",
            Phases = new System.Collections.Generic.List<PhasePlan>
            {
                new PhasePlan
                {
                    PhaseId = "phase-01",
                    PhaseNumber = 1,
                    Name = "P1",
                    Status = PhaseStatus.Completed,
                    Weight = 1.0,
                    Tasks = new System.Collections.Generic.List<TaskPlan>
                    {
                        new TaskPlan { TaskId = "task-01-01", Status = TaskPlanStatus.Passed },
                        new TaskPlan { TaskId = "task-01-02", Status = TaskPlanStatus.Passed }
                    }
                },
                new PhasePlan
                {
                    PhaseId = "phase-02",
                    PhaseNumber = 2,
                    Name = "P2",
                    Status = PhaseStatus.NotStarted,
                    Weight = 1.0,
                    Tasks = new System.Collections.Generic.List<TaskPlan>
                    {
                        new TaskPlan { TaskId = "task-02-01", Status = TaskPlanStatus.NotStarted },
                        new TaskPlan { TaskId = "task-02-02", Status = TaskPlanStatus.NotStarted }
                    }
                }
            }
        };

        var projProgress = _planningService.CalculateProjectProgress(plan);

        Assert.Equal(2, projProgress.TotalPhases);
        Assert.Equal(1, projProgress.CompletedPhases);
        Assert.Equal(50.0, projProgress.PercentComplete); // 1 / 2 = 50%
    }
}
