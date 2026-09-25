using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIBridge.Api.Dtos;
using AIBridge.Models;
using AIBridge.Services;
using AIBridge.ViewModels;
using Xunit;

namespace AIBridge.Tests;

public class HumanGateTests : IDisposable
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
    private readonly LogService _logService;
    private readonly TaskRegistry _taskRegistry;

    public HumanGateTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AIBridge_HumanGateTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _store = new FileProjectPlanStore(_testDir);

        _fakeAgent = new FakeCodingAgent();
        _registry = new CodingAgentRegistry();
        _registry.RegisterAgent(_fakeAgent);

        _logService = new LogService();
        _taskRegistry = new TaskRegistry();
        _taskService = new TaskService(_logService, _taskRegistry);
        _promptValidator = new PromptValidator();
        _promptService = new ExecutionPromptService(_promptValidator, logService: _logService);
        _humanApprovalService = new HumanApprovalService();

        _service = new CodingAgentService(_registry, _taskService, _store, _promptValidator, taskRegistry: _taskRegistry, humanApprovalService: _humanApprovalService, logService: _logService);
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

    private ProjectPlan CreateProtectedPlan()
    {
        return new ProjectPlan
        {
            ProjectId = "proj-human-07",
            Name = "Human Gate Test Project",
            Goal = "Test trusted Human Gate boundary",
            Status = ProjectPlanStatus.Approved,
            Version = 1,
            Phases = new System.Collections.Generic.List<PhasePlan>
            {
                new PhasePlan
                {
                    PhaseId = "phase-01",
                    PhaseNumber = 1,
                    Name = "Phase 1",
                    Objective = "Phase 1 Objective",
                    Status = PhaseStatus.Running,
                    Tasks = new System.Collections.Generic.List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = "task-01-01",
                            PhaseId = "phase-01",
                            TaskNumber = 1,
                            Title = "Protected Task 1",
                            Objective = "Task 1 Objective",
                            RequiresHumanApproval = true,
                            Status = TaskPlanStatus.NotStarted
                        },
                        new TaskPlan
                        {
                            TaskId = "task-01-02",
                            PhaseId = "phase-01",
                            TaskNumber = 2,
                            Title = "Protected Task 2",
                            Objective = "Task 2 Objective",
                            RequiresHumanApproval = true,
                            Status = TaskPlanStatus.NotStarted
                        }
                    }
                }
            }
        };
    }

    [Fact]
    public async Task ExternalCaller_CannotSelfApprove_DispatchBlocked()
    {
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        // Attempt dispatch without trusted approval
        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);

        Assert.False(result.Success);
        Assert.Equal(CodingAgentErrorCode.HumanApprovalRequired, result.ErrorCode);
        Assert.Equal(0, _fakeAgent.CallCount);
    }

    [Fact]
    public async Task Section7_ApiCannotSelfApprove_Returns404_AndNoApprovalCreated()
    {
        var configService = new ConfigService(_logService, Path.Combine(_testDir, "test_config.json"));
        var cfg = configService.LoadConfig();
        cfg.BridgeHost = "127.0.0.1";
        cfg.BridgePort = 9892;
        cfg.ApiToken = "test-token-human-gate-api";
        configService.SaveConfig(cfg);

        var envService = new AntigravityEnvironmentService(_logService);
        var runner = new AntigravityRunner(_logService, envService);
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        var brainService = new AIBrainService(_logService, configService, new AIBrainProviderRegistry());
        var planningService = new PlanningService(brainService, new PlanValidator(), _store);

        var server = new BridgeServer(
            configService, _logService, _taskService, _taskRegistry, envService, runner,
            planningService: planningService,
            executionPromptService: _promptService,
            codingAgentRegistry: _registry,
            codingAgentService: _service,
            humanApprovalService: _humanApprovalService);

        bool started = await server.StartAsync();
        Assert.True(started);

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{cfg.BridgePort}") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-human-gate-api");

            // Attempt to invoke removed approval endpoint
            var approveRes = await client.PostAsync("/api/projects/proj-human-07/phases/phase-01/tasks/task-01-01/approve-execution", null);
            
            // Endpoint is removed/isolated -> returns non-success (404/401)
            Assert.False(approveRes.IsSuccessStatusCode);

            // Trusted HumanApprovalRecord MUST NOT be created
            var validApproval = await _humanApprovalService.GetValidApprovalAsync("proj-human-07", "phase-01", "task-01-01", 1);
            Assert.Null(validApproval);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Section8_WpfLocalPath_ApproveExecutionCommand_CreatesTrustedRecordAndEnablesDispatch()
    {
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        var configService = new ConfigService(_logService);
        var envService = new AntigravityEnvironmentService(_logService);
        var runner = new AntigravityRunner(_logService, envService);
        var server = new BridgeServer(configService, _logService, _taskService, _taskRegistry, envService, runner);

        var brainService = new AIBrainService(_logService, configService, new AIBrainProviderRegistry());
        var planningService = new PlanningService(brainService, new PlanValidator(), _store);

        var vm = new MainViewModel(
            configService: configService,
            logService: _logService,
            taskService: _taskService,
            environmentService: envService,
            antigravityRunner: runner,
            bridgeServer: server,
            humanApprovalService: _humanApprovalService,
            planningService: planningService,
            executionPromptService: _promptService,
            codingAgentRegistry: _registry,
            codingAgentService: _service
        );

        vm.CurrentProjectPlan = plan;
        vm.WorkspacePath = _testDir;

        var taskNode = plan.Phases[0].Tasks[0];
        vm.SelectNodeDetails(taskNode);

        // Set test handler to simulate clicking YES in WPF dialog
        vm.ConfirmationDialogHandler = (msg, title) => true;

        Assert.True(vm.CanApproveExecution());

        // Execute command via WPF trusted local path
        vm.ApproveExecutionCommand.Execute(null);

        // Verify trusted HumanApprovalRecord was created
        var approval = await _humanApprovalService.GetValidApprovalAsync(plan.ProjectId, "phase-01", "task-01-01", plan.Version);
        Assert.NotNull(approval);
        Assert.Equal("LocalHuman", approval.ApprovedBy);

        // Prepare prompt and dispatch task -> Allowed
        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);
        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);

        Assert.True(result.Success);
        Assert.Equal(1, _fakeAgent.CallCount);
    }

    [Fact]
    public async Task Section9_NoApprovalConsumption_WhenSlotBusy()
    {
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        // Create valid human approval
        await _humanApprovalService.ApproveAsync(plan.ProjectId, "phase-01", "task-01-01", plan.Version, pkg.PromptId, pkg.PromptHash, "LocalHuman");

        // Occupy execution slot
        bool slotAcquired = _taskService.TryAcquireExecutionSlot();
        Assert.True(slotAcquired);

        try
        {
            // Attempt dispatch -> fails due to CONCURRENCY_CONFLICT
            var busyResult = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);

            Assert.False(busyResult.Success);
            Assert.Equal(CodingAgentErrorCode.ConcurrencyConflict, busyResult.ErrorCode);
            Assert.Equal(0, _fakeAgent.CallCount);

            // Approval MUST remain valid
            var validApproval = await _humanApprovalService.GetValidApprovalAsync(plan.ProjectId, "phase-01", "task-01-01", plan.Version, pkg.PromptId, pkg.PromptHash);
            Assert.NotNull(validApproval);
        }
        finally
        {
            _taskService.ReleaseExecutionSlot();
        }

        // Slot released -> dispatch again
        var retryResult = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);

        Assert.True(retryResult.Success);
        Assert.Equal(1, _fakeAgent.CallCount);

        // Now approval is consumed
        var consumedApproval = await _humanApprovalService.GetValidApprovalAsync(plan.ProjectId, "phase-01", "task-01-01", plan.Version, pkg.PromptId, pkg.PromptHash);
        Assert.Null(consumedApproval);
    }

    [Fact]
    public async Task Section10_NoApprovalConsumption_WhenWorkspaceInvalid()
    {
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        // Create valid human approval
        await _humanApprovalService.ApproveAsync(plan.ProjectId, "phase-01", "task-01-01", plan.Version, pkg.PromptId, pkg.PromptHash, "LocalHuman");

        string invalidWorkspace = Path.Combine(_testDir, "non_existent_directory_" + Guid.NewGuid().ToString("N"));

        // Attempt dispatch with invalid workspace
        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, invalidWorkspace);

        Assert.False(result.Success);
        Assert.Equal(CodingAgentErrorCode.WorkspaceInvalid, result.ErrorCode);
        Assert.Equal(0, _fakeAgent.CallCount);

        // Approval MUST remain valid
        var validApproval = await _humanApprovalService.GetValidApprovalAsync(plan.ProjectId, "phase-01", "task-01-01", plan.Version, pkg.PromptId, pkg.PromptHash);
        Assert.NotNull(validApproval);
    }

    [Fact]
    public async Task Section11_SuccessfulDispatchConsumesApproval()
    {
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        // Approve task
        await _humanApprovalService.ApproveAsync(plan.ProjectId, "phase-01", "task-01-01", plan.Version, pkg.PromptId, pkg.PromptHash, "LocalHuman");

        // First dispatch -> Consumes approval
        var result1 = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);
        Assert.True(result1.Success);
        Assert.Equal(1, _fakeAgent.CallCount);

        // Reset task status for second attempt
        plan.Phases[0].Tasks[0].Status = TaskPlanStatus.NotStarted;
        await _store.SavePlanAsync(plan);

        // Dispatch again without new approval -> blocked
        var result2 = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);
        Assert.False(result2.Success);
        Assert.Equal(CodingAgentErrorCode.HumanApprovalRequired, result2.ErrorCode);
        Assert.Equal(1, _fakeAgent.CallCount); // Agent call count remains 1
    }

    [Fact]
    public async Task Approval_IsTaskScoped()
    {
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        var pkg1 = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);
        var pkg2 = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-02", workspacePath: _testDir);

        // Approve task-01-01
        await _humanApprovalService.ApproveAsync(plan.ProjectId, "phase-01", "task-01-01", plan.Version);

        // Attempt dispatch task-01-02 -> Blocked
        var resultTask2 = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-02", pkg2, _testDir);

        Assert.False(resultTask2.Success);
        Assert.Equal(CodingAgentErrorCode.HumanApprovalRequired, resultTask2.ErrorCode);
        Assert.Equal(0, _fakeAgent.CallCount);
    }

    [Fact]
    public async Task Approval_IsPlanVersionScoped_StaleRejected()
    {
        var plan = CreateProtectedPlan();
        plan.Version = 1;
        await _store.SavePlanAsync(plan);

        // Approve task-01-01 on plan v1
        await _humanApprovalService.ApproveAsync(plan.ProjectId, "phase-01", "task-01-01", 1);

        // Replan updates plan to v2
        plan.Version = 2;
        await _store.SavePlanAsync(plan);

        var pkgV2 = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        // Attempt dispatch on plan v2 -> Old approval for v1 rejected
        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkgV2, _testDir);

        Assert.False(result.Success);
        Assert.Equal(CodingAgentErrorCode.HumanApprovalRequired, result.ErrorCode);
        Assert.Equal(0, _fakeAgent.CallCount);
    }

    [Fact]
    public async Task Section5_Approval_PromptHashScoped_ModifiedPromptRejected()
    {
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        // Approve with a specific promptId and promptHash
        await _humanApprovalService.ApproveAsync(plan.ProjectId, "phase-01", "task-01-01", plan.Version, "prompt-v1", "hash-original-123", "LocalHuman");

        var pkgModified = new ExecutionPromptPackage
        {
            ProjectId = plan.ProjectId,
            PhaseId = "phase-01",
            TaskId = "task-01-01",
            PlanVersion = plan.Version,
            PromptId = "prompt-v1",
            PromptHash = "hash-MODIFIED-456",
            GeneratedPrompt = "Modified prompt instructions"
        };

        // Attempt dispatch with modified prompt hash -> Rejected
        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkgModified, _testDir);

        Assert.False(result.Success);
        Assert.Equal(CodingAgentErrorCode.HumanApprovalRequired, result.ErrorCode);
        Assert.Equal(0, _fakeAgent.CallCount);
    }
}
