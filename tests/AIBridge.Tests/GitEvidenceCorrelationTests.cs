using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIBridge.Models;
using AIBridge.Services;
using Xunit;

namespace AIBridge.Tests;

public class GitEvidenceCorrelationTests : IDisposable
{
    private readonly string _tempWorkspace;
    private readonly FileProjectPlanStore _store;
    private readonly LogService _logService;
    private readonly TaskRegistry _taskRegistry;
    private readonly TaskService _taskService;
    private readonly GitCommandService _gitCommandService;
    private readonly GitEnvironmentService _gitEnvironmentService;
    private readonly GitEvidenceService _gitEvidenceService;
    private readonly PromptValidator _promptValidator;
    private readonly ExecutionPromptService _promptService;
    private readonly CodingAgentRegistry _registry;

    private readonly string _planStoreDir;

    public GitEvidenceCorrelationTests()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "AIBridge_GitCorrelation_" + Guid.NewGuid().ToString("N"));
        _planStoreDir = Path.Combine(Path.GetTempPath(), "AIBridge_PlanStore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempWorkspace);
        Directory.CreateDirectory(_planStoreDir);

        InitializeGitRepo(_tempWorkspace);

        _store = new FileProjectPlanStore(_planStoreDir);
        _logService = new LogService();
        _taskRegistry = new TaskRegistry();
        _gitCommandService = new GitCommandService(_logService);
        _gitEnvironmentService = new GitEnvironmentService(_logService, _gitCommandService);
        _gitEvidenceService = new GitEvidenceService(_logService, _gitEnvironmentService, _gitCommandService);
        _taskService = new TaskService(_logService, _taskRegistry, _gitEvidenceService);

        _promptValidator = new PromptValidator();
        _promptService = new ExecutionPromptService(_promptValidator, _gitEvidenceService, logService: _logService);

        _registry = new CodingAgentRegistry();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempWorkspace))
            {
                Directory.Delete(_tempWorkspace, recursive: true);
            }
            if (Directory.Exists(_planStoreDir))
            {
                Directory.Delete(_planStoreDir, recursive: true);
            }
        }
        catch { }
    }

    private static void InitializeGitRepo(string dir)
    {
        RunGitCmd(dir, "init");
        RunGitCmd(dir, "config user.name \"AIBridge Test\"");
        RunGitCmd(dir, "config user.email \"test@aibridge.local\"");

        string readme = Path.Combine(dir, "README.md");
        File.WriteAllText(readme, "# Test Repo\n", System.Text.Encoding.UTF8);

        RunGitCmd(dir, "add README.md");
        RunGitCmd(dir, "commit -m \"Initial commit\"");
    }

    private static void RunGitCmd(string dir, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p != null)
        {
            string outStr = p.StandardOutput.ReadToEnd();
            string errStr = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
        }
    }

    private ProjectPlan CreateTestPlan(string taskId = "task-git-01")
    {
        return new ProjectPlan
        {
            ProjectId = "proj-git-evidence-07",
            Name = "Git Evidence Correlation Test",
            Goal = "Test evidence correlation",
            Status = ProjectPlanStatus.Approved,
            Version = 1,
            Phases = new System.Collections.Generic.List<PhasePlan>
            {
                new PhasePlan
                {
                    PhaseId = "phase-01",
                    PhaseNumber = 1,
                    Name = "Phase 1",
                    Status = PhaseStatus.Running,
                    Tasks = new System.Collections.Generic.List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = taskId,
                            PhaseId = "phase-01",
                            TaskNumber = 1,
                            Title = "Git Task",
                            Objective = "Objective for Git Task",
                            Status = TaskPlanStatus.NotStarted
                        }
                    }
                }
            }
        };
    }

    [Fact]
    public async Task GitEvidence_RuntimeAgentTaskId_CorrelatedToReviewPackage()
    {
        var plan = CreateTestPlan("task-git-01");
        await _store.SavePlanAsync(plan);

        // Custom fake agent that creates a file in the workspace
        var customAgent = new FileCreatingFakeAgent(_taskService, filePath: Path.Combine(_tempWorkspace, "test-output.txt"), content: "Test output file");
        _registry.RegisterAgent(customAgent);

        var service = new CodingAgentService(_registry, _taskService, _store, _promptValidator, _gitEvidenceService, _taskRegistry, logService: _logService);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-git-01", workspacePath: _tempWorkspace);

        var result = await service.DispatchTaskAsync(plan, "phase-01", "task-git-01", pkg, _tempWorkspace);

        Assert.True(result.Success);

        // Verify runtime AgentTaskId was set and differs from TaskPlan.TaskId ("task-git-01")
        Assert.False(string.IsNullOrWhiteSpace(result.AgentTaskId));
        Assert.NotEqual("task-git-01", result.AgentTaskId);

        // Retrieve ExecutionReviewPackage
        var reviewPkg = service.GetReviewPackage(result.ExecutionId);
        Assert.NotNull(reviewPkg);
        Assert.NotNull(reviewPkg.GitEvidence);
        Assert.True(reviewPkg.GitEvidence.IsRepository);

        // Verify Git evidence was retrieved for the exact execution
        Assert.Contains(reviewPkg.ChangedFiles, f => f.EndsWith("test-output.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GitEvidence_MultipleAttempts_EvidenceIsolatedPerAttempt()
    {
        var plan = CreateTestPlan("task-git-multi");
        await _store.SavePlanAsync(plan);

        // Attempt 1 creates file1.txt
        var agentAttempt1 = new FileCreatingFakeAgent(_taskService, filePath: Path.Combine(_tempWorkspace, "file1.txt"), content: "Attempt 1 content");
        _registry.RegisterAgent(agentAttempt1);

        var service = new CodingAgentService(_registry, _taskService, _store, _promptValidator, _gitEvidenceService, _taskRegistry, logService: _logService);

        var pkg1 = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-git-multi", workspacePath: _tempWorkspace);
        var result1 = await service.DispatchTaskAsync(plan, "phase-01", "task-git-multi", pkg1, _tempWorkspace);

        Assert.True(result1.Success);

        var reviewPkg1 = service.GetReviewPackage(result1.ExecutionId);
        Assert.NotNull(reviewPkg1);
        Assert.NotNull(reviewPkg1.GitEvidence);
        Assert.Equal(1, reviewPkg1.AttemptNumber);

        // Reset task status for Attempt 2
        plan.Phases[0].Tasks[0].Status = TaskPlanStatus.NotStarted;
        await _store.SavePlanAsync(plan);

        // Commit Attempt 1 changes so Attempt 2 starts clean
        RunGitCmd(_tempWorkspace, "add file1.txt");
        RunGitCmd(_tempWorkspace, "commit -m \"commit attempt 1\"");

        // Attempt 2 creates file2.txt
        var agentAttempt2 = new FileCreatingFakeAgent(_taskService, filePath: Path.Combine(_tempWorkspace, "file2.txt"), content: "Attempt 2 content");
        _registry.RegisterAgent(agentAttempt2);

        var pkg2 = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-git-multi", workspacePath: _tempWorkspace);
        var result2 = await service.DispatchTaskAsync(plan, "phase-01", "task-git-multi", pkg2, _tempWorkspace);

        Assert.True(result2.Success);

        var reviewPkg2 = service.GetReviewPackage(result2.ExecutionId);
        Assert.NotNull(reviewPkg2);
        Assert.NotNull(reviewPkg2.GitEvidence);
        Assert.Equal(2, reviewPkg2.AttemptNumber);

        // Assert Attempt 1 evidence contains file1.txt and NOT file2.txt
        Assert.Contains(reviewPkg1.ChangedFiles, f => f.EndsWith("file1.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reviewPkg1.ChangedFiles, f => f.EndsWith("file2.txt", StringComparison.OrdinalIgnoreCase));

        // Assert Attempt 2 evidence contains file2.txt and NOT file1.txt
        Assert.Contains(reviewPkg2.ChangedFiles, f => f.EndsWith("file2.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reviewPkg2.ChangedFiles, f => f.EndsWith("file1.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GitEvidence_NoChanges_Vs_EvidenceFailure_Distinction()
    {
        var plan = CreateTestPlan("task-no-changes");
        await _store.SavePlanAsync(plan);

        // Agent does nothing (no file changes)
        var noChangeAgent = new NoOpFakeAgent(_taskService);
        _registry.RegisterAgent(noChangeAgent);

        var service = new CodingAgentService(_registry, _taskService, _store, _promptValidator, _gitEvidenceService, _taskRegistry, logService: _logService);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-no-changes", workspacePath: _tempWorkspace);
        var result = await service.DispatchTaskAsync(plan, "phase-01", "task-no-changes", pkg, _tempWorkspace);

        Assert.True(result.Success);

        var reviewPkg = service.GetReviewPackage(result.ExecutionId);
        Assert.NotNull(reviewPkg);
        Assert.NotNull(reviewPkg.GitEvidence);
        Assert.True(reviewPkg.GitEvidence.IsRepository);
        Assert.Equal("Ready", reviewPkg.GitEvidence.Status);
        Assert.Empty(reviewPkg.ChangedFiles); // 0 files changed, evidence is NOT null
    }

    [Fact]
    public async Task RealGitEvidence_Requirement_Section35()
    {
        // Section 35 required real git evidence test
        var plan = CreateTestPlan("task-real-git");
        await _store.SavePlanAsync(plan);

        string expectedFilename = "phase07-evidence-correlation.txt";
        string expectedContent = "AIBridge Phase 07 Git Evidence";

        var realGitAgent = new FileCreatingFakeAgent(_taskService, filePath: Path.Combine(_tempWorkspace, expectedFilename), content: expectedContent);
        _registry.RegisterAgent(realGitAgent);

        var service = new CodingAgentService(_registry, _taskService, _store, _promptValidator, _gitEvidenceService, _taskRegistry, logService: _logService);

        var pkg = await _promptService.PreparePromptPackageAsync(plan, "phase-01", "task-real-git", workspacePath: _tempWorkspace);
        var result = await service.DispatchTaskAsync(plan, "phase-01", "task-real-git", pkg, _tempWorkspace);

        Assert.True(result.Success);

        var reviewPkg = service.GetReviewPackage(result.ExecutionId);
        Assert.NotNull(reviewPkg);
        Assert.NotNull(reviewPkg.GitEvidence);
        Assert.NotNull(reviewPkg.GitEvidence.BeforeSnapshot);
        Assert.NotNull(reviewPkg.GitEvidence.AfterSnapshot);

        Assert.Contains(reviewPkg.ChangedFiles, f => f.EndsWith(expectedFilename, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result.ExecutionId, reviewPkg.ExecutionId);
    }
}

public class FileCreatingFakeAgent : ICodingAgent
{
    private readonly ITaskService _taskService;
    private readonly string _filePath;
    private readonly string _content;

    public string Id => "fake-file-agent";
    public CodingAgentDescriptor Descriptor => new() { Id = Id, DisplayName = "File Creating Fake Agent" };

    public FileCreatingFakeAgent(ITaskService taskService, string filePath, string content)
    {
        _taskService = taskService;
        _filePath = filePath;
        _content = content;
    }

    public async Task<CodingAgentExecutionResult> ExecuteAsync(CodingAgentExecutionRequest request, System.Threading.CancellationToken cancellationToken = default)
    {
        var agentTask = _taskService.CreateTask(request.Prompt, request.WorkspacePath);
        
        // Execute fake runner that writes file
        var runner = new CustomFileRunner(_filePath, _content);
        var result = await _taskService.SubmitTaskAsync(agentTask, runner, cancellationToken);

        return new CodingAgentExecutionResult
        {
            ExecutionId = request.ExecutionId,
            AgentTaskId = agentTask.Id,
            AgentId = Id,
            Success = result.Success,
            ExitCode = result.ExitCode,
            Stdout = result.StandardOutput ?? "File created successfully",
            StartedAt = DateTime.Now,
            CompletedAt = DateTime.Now
        };
    }
}

public class NoOpFakeAgent : ICodingAgent
{
    private readonly ITaskService _taskService;
    public string Id => "fake-noop-agent";
    public CodingAgentDescriptor Descriptor => new() { Id = Id, DisplayName = "No-Op Fake Agent" };

    public NoOpFakeAgent(ITaskService taskService)
    {
        _taskService = taskService;
    }

    public async Task<CodingAgentExecutionResult> ExecuteAsync(CodingAgentExecutionRequest request, System.Threading.CancellationToken cancellationToken = default)
    {
        var agentTask = _taskService.CreateTask(request.Prompt, request.WorkspacePath);
        var runner = new NoOpRunner();
        var result = await _taskService.SubmitTaskAsync(agentTask, runner, cancellationToken);

        return new CodingAgentExecutionResult
        {
            ExecutionId = request.ExecutionId,
            AgentTaskId = agentTask.Id,
            AgentId = Id,
            Success = result.Success,
            ExitCode = 0,
            Stdout = "No changes made",
            StartedAt = DateTime.Now,
            CompletedAt = DateTime.Now
        };
    }
}

public class CustomFileRunner : ICodingAgentRunner
{
    private readonly string _filePath;
    private readonly string _content;

    public string Name => "CustomFileRunner";

    public CustomFileRunner(string filePath, string content)
    {
        _filePath = filePath;
        _content = content;
    }

    public Task<(bool IsValid, string Message)> ValidateConfigurationAsync(string agentPath)
    {
        return Task.FromResult((true, "Valid"));
    }

    public Task<(bool IsValid, string Message)> ValidateWorkspaceAsync(string workspacePath)
    {
        return Task.FromResult((true, "Valid"));
    }

    public Task<AgentResult> ExecuteAsync(AgentTask task, System.Threading.CancellationToken cancellationToken = default)
    {
        File.WriteAllText(_filePath, _content, System.Text.Encoding.UTF8);
        return Task.FromResult(new AgentResult
        {
            Success = true,
            ExitCode = 0,
            StandardOutput = $"Created {_filePath}"
        });
    }
}

public class NoOpRunner : ICodingAgentRunner
{
    public string Name => "NoOpRunner";

    public Task<(bool IsValid, string Message)> ValidateConfigurationAsync(string agentPath)
    {
        return Task.FromResult((true, "Valid"));
    }

    public Task<(bool IsValid, string Message)> ValidateWorkspaceAsync(string workspacePath)
    {
        return Task.FromResult((true, "Valid"));
    }

    public Task<AgentResult> ExecuteAsync(AgentTask task, System.Threading.CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentResult
        {
            Success = true,
            ExitCode = 0,
            StandardOutput = "No-op runner finished"
        });
    }
}
