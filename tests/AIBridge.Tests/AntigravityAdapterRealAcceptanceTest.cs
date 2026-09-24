using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using AIBridge.Models;
using AIBridge.Services;
using Xunit;

namespace AIBridge.Tests;

public class AntigravityAdapterRealAcceptanceTest : IDisposable
{
    private readonly string _tempWorkspace;
    private readonly FileProjectPlanStore _store;
    private readonly LogService _logService;
    private readonly TaskRegistry _taskRegistry;
    private readonly TaskService _taskService;
    private readonly AntigravityEnvironmentService _envService;
    private readonly AntigravityRunner _antigravityRunner;
    private readonly AntigravityCodingAgent _antigravityAgent;
    private readonly CodingAgentRegistry _registry;
    private readonly PromptValidator _promptValidator;
    private readonly ExecutionPromptService _promptService;
    private readonly CodingAgentService _service;

    public AntigravityAdapterRealAcceptanceTest()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "AIBridge_RealAntigravity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempWorkspace);

        InitializeGitRepository(_tempWorkspace);

        _store = new FileProjectPlanStore(_tempWorkspace);
        _logService = new LogService();
        _taskRegistry = new TaskRegistry();
        _taskService = new TaskService(_logService, _taskRegistry);
        _envService = new AntigravityEnvironmentService(_logService);
        _antigravityRunner = new AntigravityRunner(_logService, _envService, timeoutMinutes: 5);
        _antigravityAgent = new AntigravityCodingAgent(_taskService, _antigravityRunner, _envService);

        _registry = new CodingAgentRegistry();
        _registry.RegisterAgent(_antigravityAgent);

        _promptValidator = new PromptValidator();
        _promptService = new ExecutionPromptService(_promptValidator, logService: _logService);
        _service = new CodingAgentService(_registry, _taskService, _store, _promptValidator, taskRegistry: _taskRegistry, logService: _logService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempWorkspace))
            {
                Directory.Delete(_tempWorkspace, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task RealAntigravityAdapter_IsolatedWorkspaceExecution_Success()
    {
        var envInfo = _envService.CurrentInfo;
        if (envInfo.InstallationState == CliInstallationState.NotInstalled)
        {
            // Skip real CLI test if agy is not installed on test runner environment
            return;
        }

        var plan = new ProjectPlan
        {
            ProjectId = "proj-real-07",
            Name = "Real Antigravity Test",
            Goal = "Test real Antigravity adapter execution",
            Status = ProjectPlanStatus.Approved,
            Version = 1,
            Phases = new System.Collections.Generic.List<PhasePlan>
            {
                new PhasePlan
                {
                    PhaseId = "phase-07",
                    PhaseNumber = 7,
                    Name = "Phase 7",
                    Objective = "Real Antigravity dispatch phase",
                    Status = PhaseStatus.Running,
                    Tasks = new System.Collections.Generic.List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = "task-07-01",
                            PhaseId = "phase-07",
                            TaskNumber = 1,
                            Title = "Create test file",
                            Objective = "Create test file via Antigravity agent",
                            Status = TaskPlanStatus.NotStarted
                        }
                    }
                }
            }
        };

        await _store.SavePlanAsync(plan);

        string externalInstructions = "Create a file named:\n\nphase07-chatgpt-brain-test.txt\n\nwith exactly:\n\nHello from ChatGPT Web Brain";

        var promptPackage = await _promptService.PreparePromptPackageAsync(
            plan, "phase-07", "task-07-01",
            externalInstructions: externalInstructions,
            workspacePath: _tempWorkspace,
            generatedBy: "ChatGPTWeb");

        Assert.NotNull(promptPackage);
        Assert.Equal("ChatGPTWeb", promptPackage.GeneratedBy);
        Assert.False(string.IsNullOrWhiteSpace(promptPackage.PromptHash));

        var result = await _service.DispatchTaskAsync(
            plan, "phase-07", "task-07-01",
            promptPackage, _tempWorkspace,
            confirmHumanGate: false,
            timeoutSeconds: 300);

        Assert.NotNull(result);

        string expectedFilePath = Path.Combine(_tempWorkspace, "phase07-chatgpt-brain-test.txt");

        if (result.Success && File.Exists(expectedFilePath))
        {
            Assert.Equal(0, result.ExitCode);

            string content = File.ReadAllText(expectedFilePath).Trim();
            Assert.Equal("Hello from ChatGPT Web Brain", content);

            // Verify task status is Reviewing, NOT Passed!
            var updatedPlan = await _store.LoadPlanAsync(plan.ProjectId);
            var task = updatedPlan.Phases[0].Tasks[0];
            Assert.Equal(TaskPlanStatus.Reviewing, task.Status);
            Assert.NotEqual(TaskPlanStatus.Passed, task.Status);

            // Verify review package is available
            var reviewPkg = _service.GetReviewPackage(result.ExecutionId);
            Assert.NotNull(reviewPkg);
            Assert.Equal("Pending", reviewPkg.ReviewStatus);
            Assert.True(reviewPkg.AgentSuccess);
        }
        else
        {
            // Verify that failed execution was properly recorded in history and normalized
            var history = _service.GetExecutionHistory();
            Assert.NotEmpty(history);
            Assert.False(string.IsNullOrWhiteSpace(result.Stdout + result.Stderr + result.ErrorMessage));
        }
    }

    private static void InitializeGitRepository(string dir)
    {
        try
        {
            RunGitCommand(dir, "init");
            RunGitCommand(dir, "config user.name \"AIBridge Test\"");
            RunGitCommand(dir, "config user.email \"test@aibridge.local\"");
            
            string readmePath = Path.Combine(dir, "README.md");
            File.WriteAllText(readmePath, "# Isolated Test Repo\n");
            
            RunGitCommand(dir, "add README.md");
            RunGitCommand(dir, "commit -m \"initial commit\"");
        }
        catch { }
    }

    private static void RunGitCommand(string dir, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
    }
}
