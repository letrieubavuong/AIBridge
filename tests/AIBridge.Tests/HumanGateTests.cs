using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using AIBridge.Api.Dtos;
using AIBridge.Models;
using AIBridge.Services;
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

    public HumanGateTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AIBridge_HumanGateTest_" + Guid.NewGuid().ToString("N"));
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
    public async Task TrustedHumanApproval_AllowsDispatch()
    {
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        // Create trusted approval
        await _humanApprovalService.ApproveAsync(new HumanApprovalRequest
        {
            ProjectId = plan.ProjectId,
            PhaseId = "phase-01",
            TaskId = "task-01-01",
            PlanVersion = plan.Version,
            ApprovedBy = "LocalHuman"
        });

        // Dispatch now allowed
        var result = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);

        Assert.True(result.Success);
        Assert.Equal(1, _fakeAgent.CallCount);
    }

    [Fact]
    public async Task Approval_IsTaskScoped()
    {
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        var pkg1 = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);
        var pkg2 = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-02", workspacePath: _testDir);

        // Approve task-01-01
        await _humanApprovalService.ApproveAsync(new HumanApprovalRequest
        {
            ProjectId = plan.ProjectId,
            PhaseId = "phase-01",
            TaskId = "task-01-01",
            PlanVersion = plan.Version
        });

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
        await _humanApprovalService.ApproveAsync(new HumanApprovalRequest
        {
            ProjectId = plan.ProjectId,
            PhaseId = "phase-01",
            TaskId = "task-01-01",
            PlanVersion = 1
        });

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
    public async Task Approval_SingleUseConsumption()
    {
        var plan = CreateProtectedPlan();
        await _store.SavePlanAsync(plan);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", workspacePath: _testDir);

        // Approve task
        await _humanApprovalService.ApproveAsync(new HumanApprovalRequest
        {
            ProjectId = plan.ProjectId,
            PhaseId = "phase-01",
            TaskId = "task-01-01",
            PlanVersion = plan.Version
        });

        // First dispatch -> Consumes approval
        var result1 = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);
        Assert.True(result1.Success);
        Assert.Equal(1, _fakeAgent.CallCount);

        // Task fails or reset for retry, attempt second dispatch without new approval
        plan.Phases[0].Tasks[0].Status = TaskPlanStatus.NotStarted;
        await _store.SavePlanAsync(plan);

        var result2 = await _service.DispatchTaskAsync(plan, "phase-01", "task-01-01", pkg, _testDir);
        Assert.False(result2.Success);
        Assert.Equal(CodingAgentErrorCode.HumanApprovalRequired, result2.ErrorCode);
        Assert.Equal(1, _fakeAgent.CallCount); // Call count remains 1
    }

    [Fact]
    public async Task BridgeServer_HumanGateApiTrustBoundary_Tests()
    {
        var logService = new LogService();
        var configService = new ConfigService(logService);
        var cfg = configService.LoadConfig();
        cfg.BridgeHost = "127.0.0.1";
        cfg.BridgePort = 9888;
        cfg.ApiToken = "test-token-human-gate";
        configService.SaveConfig(cfg);
        var taskRegistry = new TaskRegistry();
        var taskService = new TaskService(logService, taskRegistry);
        var envService = new AntigravityEnvironmentService(logService);
        var runner = new AntigravityRunner(logService, envService);

        var planStore = new FileProjectPlanStore(_testDir);
        var plan = CreateProtectedPlan();
        await planStore.SavePlanAsync(plan);

        var brainService = new AIBrainService(logService, configService, new AIBrainProviderRegistry());
        var planningService = new PlanningService(brainService, new PlanValidator(), planStore);
        var promptService = new ExecutionPromptService(new PromptValidator(), logService: logService);
        var humanApprovalService = new HumanApprovalService();
        var codingAgentService = new CodingAgentService(_registry, taskService, planStore, new PromptValidator(), taskRegistry: taskRegistry, humanApprovalService: humanApprovalService, logService: logService);

        var server = new BridgeServer(
            configService, logService, taskService, taskRegistry, envService, runner,
            planningService: planningService,
            executionPromptService: promptService,
            codingAgentRegistry: _registry,
            codingAgentService: codingAgentService,
            humanApprovalService: humanApprovalService);

        bool started = await server.StartAsync();
        Assert.True(started);

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9888") };

            // 1. Unauthenticated approve-execution request -> REJECTED 401
            var unauthRes = await client.PostAsync("/api/projects/proj-human-07/phases/phase-01/tasks/task-01-01/approve-execution", null);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

            // 2. Authenticated ordinary dispatch request without prior approval -> REJECTED 400 HUMAN_APPROVAL_REQUIRED
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-human-gate");
            var dispatchRes = await client.PostAsJsonAsync("/api/projects/proj-human-07/phases/phase-01/tasks/task-01-01/dispatch", new { timeoutSeconds = 600 });
            Assert.Equal(HttpStatusCode.BadRequest, dispatchRes.StatusCode);
            var dispatchBody = await dispatchRes.Content.ReadAsStringAsync();
            Assert.Contains("HUMAN_APPROVAL_REQUIRED", dispatchBody);
            Assert.Equal(0, _fakeAgent.CallCount);

            // 3. Trusted local human approval endpoint -> ACCEPTED 200 OK
            var approveRes = await client.PostAsync("/api/projects/proj-human-07/phases/phase-01/tasks/task-01-01/approve-execution", null);
            Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

            // 4. Dispatch after trusted approval -> ACCEPTED 200 OK
            var dispatchAllowedRes = await client.PostAsJsonAsync("/api/projects/proj-human-07/phases/phase-01/tasks/task-01-01/dispatch", new { timeoutSeconds = 600 });
            Assert.Equal(HttpStatusCode.OK, dispatchAllowedRes.StatusCode);
            Assert.Equal(1, _fakeAgent.CallCount);
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
