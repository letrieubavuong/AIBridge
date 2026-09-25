using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;
using AIBridge.Services;
using Xunit;

namespace AIBridge.Tests;

public class CodingAgentServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileProjectPlanStore _store;
    private readonly FakeCodingAgent _fakeAgent;
    private readonly CodingAgentRegistry _registry;
    private readonly TaskService _taskService;
    private readonly PromptValidator _promptValidator;
    private readonly ExecutionPromptService _promptService;
    private readonly HumanApprovalService _humanApprovalService;
    private readonly CodingAgentService _service;

    public CodingAgentServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AIBridge_CodingAgentTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _store = new FileProjectPlanStore(_testDir);

        _fakeAgent = new FakeCodingAgent();
        _registry = new CodingAgentRegistry();
        _registry.RegisterAgent(_fakeAgent);

        var logService = new LogService();
        var taskRegistry = new TaskRegistry();
        _taskService = new TaskService(logService, taskRegistry);
        _promptValidator = new PromptValidator();
        _promptService = new ExecutionPromptService(_promptValidator, logService: logService);
        _humanApprovalService = new HumanApprovalService();

        _service = new CodingAgentService(_registry, _taskService, _store, _promptValidator, taskRegistry: taskRegistry, humanApprovalService: _humanApprovalService, logService: logService);
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
    public async Task Dispatch_UnapprovedPlan_ReturnsTaskNotDispatchable()
    {
        var plan = CreateTestPlan();
        plan.Status = ProjectPlanStatus.Draft; // Unapproved
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);

        Assert.False(result.Success);
        Assert.Equal(CodingAgentErrorCode.TaskNotDispatchable, result.ErrorCode);
        Assert.Equal(0, _fakeAgent.CallCount);
    }

    [Fact]
    public async Task Dispatch_UnsatisfiedTaskDependency_ReturnsDependencyNotSatisfied()
    {
        var plan = CreateTestPlan();
        plan.Status = ProjectPlanStatus.Approved;
        plan.Phases[0].Tasks[0].Status = TaskPlanStatus.NotStarted; // Dependency Task 1.1 not passed yet
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-02", workspacePath: _testDir);

        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-02", pkg, _testDir);

        Assert.False(result.Success);
        Assert.Equal(CodingAgentErrorCode.DependencyNotSatisfied, result.ErrorCode);
        Assert.Equal(0, _fakeAgent.CallCount);

        // Mark Task 1.1 Passed -> Task 1.2 becomes eligible
        plan.Phases[0].Tasks[0].Status = TaskPlanStatus.Passed;
        await _store.SavePlanAsync(plan);

        var successResult = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-02", pkg, _testDir);
        Assert.True(successResult.Success);
        Assert.Equal(1, _fakeAgent.CallCount);
    }

    [Fact]
    public async Task Dispatch_UnsatisfiedPhaseDependency_ReturnsDependencyNotSatisfied()
    {
        var plan = CreateMultiPhasePlan();
        plan.Status = ProjectPlanStatus.Approved;
        plan.Phases[0].Status = PhaseStatus.Running; // Phase 1 not completed yet
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-02", "task-02-01", workspacePath: _testDir);

        var result = await _service.DispatchTaskAsync(plan, "phase-02", "task-02-01", pkg, _testDir);

        Assert.False(result.Success);
        Assert.Equal(CodingAgentErrorCode.DependencyNotSatisfied, result.ErrorCode);
        Assert.Equal(0, _fakeAgent.CallCount);

        // Mark Phase 1 Completed -> Phase 2 task becomes eligible
        plan.Phases[0].Status = PhaseStatus.Completed;
        await _store.SavePlanAsync(plan);

        var successResult = await _service.DispatchTaskAsync(plan, "phase-02", "task-02-01", pkg, _testDir);
        Assert.True(successResult.Success);
        Assert.Equal(1, _fakeAgent.CallCount);
    }

    [Fact]
    public async Task Dispatch_HumanGateRequired_WithoutHumanApproval_Blocked()
    {
        var plan = CreateTestPlan();
        plan.Status = ProjectPlanStatus.Approved;
        plan.Phases[0].Tasks[0].RequiresHumanApproval = true;
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        // Dispatch without trusted approval -> Blocked
        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);

        Assert.False(result.Success);
        Assert.Equal(CodingAgentErrorCode.HumanApprovalRequired, result.ErrorCode);
        Assert.Equal(0, _fakeAgent.CallCount);

        // Approve via trusted HumanApprovalService -> Allowed
        await _humanApprovalService.ApproveAsync(new HumanApprovalRequest
        {
            ProjectId = plan.ProjectId,
            PhaseId = "phase-01",
            TaskId = "task-01-01",
            PlanVersion = plan.Version
        });

        var allowedResult = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);
        Assert.True(allowedResult.Success);
        Assert.Equal(1, _fakeAgent.CallCount);
    }

    [Fact]
    public async Task Dispatch_StalePromptPackage_ReturnsPromptStale()
    {
        var plan = CreateTestPlan();
        plan.Status = ProjectPlanStatus.Approved;
        plan.Version = 1;
        await _store.SavePlanAsync(plan);

        var pkgV1 = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        // Plan updated to v2
        plan.Version = 2;
        await _store.SavePlanAsync(plan);

        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkgV1, _testDir);

        Assert.False(result.Success);
        Assert.Equal(CodingAgentErrorCode.PromptStale, result.ErrorCode);
        Assert.Equal(0, _fakeAgent.CallCount);
    }

    [Fact]
    public async Task Dispatch_SuccessfulExecution_SetsStatusToReviewing_NotPassed()
    {
        var plan = CreateTestPlan();
        plan.Status = ProjectPlanStatus.Approved;
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        _fakeAgent.SimulatedResult = new CodingAgentExecutionResult
        {
            Success = true,
            ExitCode = 0,
            Stdout = "Agent execution finished cleanly."
        };

        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);

        // MUST BE SET TO REVIEWING (NOT PASSED!)
        var reloadedPlan = await _store.LoadPlanAsync(plan.ProjectId);
        var task = reloadedPlan.Phases[0].Tasks[0];
        Assert.Equal(TaskPlanStatus.Reviewing, task.Status);
        Assert.NotEqual(TaskPlanStatus.Passed, task.Status);

        // Check Review Package
        var reviewPkg = _service.GetReviewPackage(result.ExecutionId);
        Assert.NotNull(reviewPkg);
        Assert.True(reviewPkg.AgentSuccess);
        Assert.Equal("Pending", reviewPkg.ReviewStatus);
    }

    [Fact]
    public async Task Dispatch_FailedExecution_SetsStatusToFailed_NoAutoRetry()
    {
        var plan = CreateTestPlan();
        plan.Status = ProjectPlanStatus.Approved;
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        _fakeAgent.SimulatedResult = new CodingAgentExecutionResult
        {
            Success = false,
            ExitCode = 1,
            ErrorMessage = "Compilation error in generated file."
        };

        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);

        Assert.False(result.Success);
        Assert.Equal(1, _fakeAgent.CallCount); // Only 1 attempt, no auto retry!

        var reloadedPlan = await _store.LoadPlanAsync(plan.ProjectId);
        var task = reloadedPlan.Phases[0].Tasks[0];
        Assert.Equal(TaskPlanStatus.Failed, task.Status);
    }

    [Fact]
    public async Task Dispatch_Cancellation_PropagatesCancellation()
    {
        var plan = CreateTestPlan();
        plan.Status = ProjectPlanStatus.Approved;
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        using var cts = new CancellationTokenSource();
        _fakeAgent.SimulateDelayMs = 2000;
        _fakeAgent.CancelTokenAction = () => cts.Cancel();

        var taskDispatch = _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir, cancellationToken: cts.Token);

        var result = await taskDispatch;

        Assert.False(result.Success);
        Assert.True(result.WasCancelled);
        Assert.Equal(CodingAgentErrorCode.AgentCancelled, result.ErrorCode);
    }

    [Fact]
    public async Task Dispatch_VietnameseAndEmoji_PreservesExactUnicodeNoMojibake()
    {
        var plan = CreateTestPlan();
        plan.Status = ProjectPlanStatus.Approved;
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        string expectedVietnameseOutput = "Tiếng Việt kiểm tra.\nNguyên nhân: cấu hình không đúng.\nCách khắc phục: sửa cấu hình và chạy lại.\nĐường dẫn thử nghiệm.\nHoàn thành thành công.\n🤖 🚀 ✅";

        _fakeAgent.SimulatedResult = new CodingAgentExecutionResult
        {
            Success = true,
            ExitCode = 0,
            Stdout = expectedVietnameseOutput,
            Stderr = "Lỗi thử nghiệm tiếng Việt: không có lỗi."
        };

        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);

        Assert.True(result.Success);
        Assert.Equal(expectedVietnameseOutput, result.Stdout);
        Assert.Contains("Nguyên nhân", result.Stdout);
        Assert.Contains("Cách khắc phục", result.Stdout);
        Assert.Contains("🤖 🚀 ✅", result.Stdout);

        // Ensure NO mojibake characters
        Assert.DoesNotContain("Ã", result.Stdout);
        Assert.DoesNotContain("Â", result.Stdout);
        Assert.DoesNotContain("áº", result.Stdout);
        Assert.DoesNotContain("á»", result.Stdout);

        // Verify ReviewPackage preserves Unicode
        var reviewPkg = _service.GetReviewPackage(result.ExecutionId);
        Assert.NotNull(reviewPkg);
        Assert.Equal(expectedVietnameseOutput, reviewPkg.OutputSummary);
        Assert.DoesNotContain("Ã", reviewPkg.OutputSummary);
    }

    [Fact]
    public async Task Dispatch_Timeout_ReturnsTimedOut()
    {
        var plan = CreateTestPlan();
        plan.Status = ProjectPlanStatus.Approved;
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        _fakeAgent.SimulateDelayMs = 3000;

        // Dispatch with 1 second timeout
        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir, timeoutSeconds: 1);

        Assert.False(result.Success);
        Assert.True(result.TimedOut);
        Assert.Equal(CodingAgentErrorCode.AgentTimeout, result.ErrorCode);
    }

    private static ProjectPlan CreateTestPlan()
    {
        return new ProjectPlan
        {
            ProjectId = "proj-coding-01",
            Name = "Coding Test Project",
            Goal = "Coding Test Goal",
            Status = ProjectPlanStatus.Approved,
            Version = 1,
            Phases = new List<PhasePlan>
            {
                new PhasePlan
                {
                    PhaseId = "phase-01",
                    PhaseNumber = 1,
                    Name = "Phase 1",
                    Objective = "Phase 1 Obj",
                    Status = PhaseStatus.Completed,
                    Weight = 1.0,
                    Tasks = new List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = "task-01-01",
                            PhaseId = "phase-01",
                            TaskNumber = 1,
                            Title = "Task 1.1",
                            Objective = "Obj 1.1",
                            Status = TaskPlanStatus.NotStarted
                        },
                        new TaskPlan
                        {
                            TaskId = "task-01-02",
                            PhaseId = "phase-01",
                            TaskNumber = 2,
                            Title = "Task 1.2",
                            Objective = "Obj 1.2",
                            Status = TaskPlanStatus.NotStarted,
                            Dependencies = new List<string> { "task-01-01" }
                        }
                    }
                }
            }
        };
    }

    private static ProjectPlan CreateMultiPhasePlan()
    {
        var plan = CreateTestPlan();
        plan.Phases.Add(new PhasePlan
        {
            PhaseId = "phase-02",
            PhaseNumber = 2,
            Name = "Phase 2",
            Objective = "Phase 2 Obj",
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
                    Title = "Task 2.1",
                    Objective = "Obj 2.1",
                    Status = TaskPlanStatus.NotStarted
                }
            }
        });
        return plan;
    }
}

public class FakeCodingAgent : ICodingAgent
{
    public string Id => "antigravity";
    public CodingAgentDescriptor Descriptor { get; } = new()
    {
        Id = "antigravity",
        DisplayName = "Fake Agent",
        IsAvailable = true,
        IsConfigured = true
    };

    public int CallCount { get; private set; }
    public CodingAgentExecutionResult? SimulatedResult { get; set; }
    public int SimulateDelayMs { get; set; } = 10;
    public Action? CancelTokenAction { get; set; }

    public async Task<CodingAgentExecutionResult> ExecuteAsync(CodingAgentExecutionRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (SimulateDelayMs > 0)
        {
            CancelTokenAction?.Invoke();
            await Task.Delay(SimulateDelayMs, cancellationToken);
        }

        if (SimulatedResult != null)
        {
            SimulatedResult.ExecutionId = request.ExecutionId;
            SimulatedResult.AgentId = Id;
            return SimulatedResult;
        }

        return new CodingAgentExecutionResult
        {
            ExecutionId = request.ExecutionId,
            AgentId = Id,
            Success = true,
            ExitCode = 0,
            Stdout = "Fake execution output.",
            StartedAt = request.RequestedAt,
            CompletedAt = DateTime.Now
        };
    }
}
