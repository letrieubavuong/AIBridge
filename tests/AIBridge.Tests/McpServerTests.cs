using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using AIBridge.Models;
using AIBridge.Services;
using McpServer = AIBridge.Services.McpServer;
using Xunit;

namespace AIBridge.Tests;

public class McpServerTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileProjectPlanStore _store;
    private readonly FakeCodingAgent _fakeAgent;
    private readonly CodingAgentRegistry _registry;
    private readonly TaskService _taskService;
    private readonly PromptValidator _promptValidator;
    private readonly ExecutionPromptService _promptService;
    private readonly HumanApprovalService _humanApprovalService;
    private readonly CodingAgentService _codingAgentService;
    private readonly PlanningService _planningService;
    private readonly ConfigService _configService;
    private readonly LogService _logService;
    private readonly TaskRegistry _taskRegistry;

    public McpServerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AIBridge_McpServerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _logService = new LogService();
        _configService = new ConfigService(_logService);
        var cfg = _configService.LoadConfig();
        cfg.McpHost = "127.0.0.1";
        cfg.McpPort = 8799;
        cfg.ApiToken = "test-token-mcp-phase08";
        cfg.WorkspacePath = _testDir;
        _configService.SaveConfig(cfg);

        _store = new FileProjectPlanStore(_testDir);
        _fakeAgent = new FakeCodingAgent();
        _registry = new CodingAgentRegistry();
        _registry.RegisterAgent(_fakeAgent);

        _taskRegistry = new TaskRegistry();
        _taskService = new TaskService(_logService, _taskRegistry);
        _promptValidator = new PromptValidator();
        _promptService = new ExecutionPromptService(_promptValidator, logService: _logService);
        _humanApprovalService = new HumanApprovalService();

        var brainService = new AIBrainService(_logService, _configService, new AIBrainProviderRegistry());
        _planningService = new PlanningService(brainService, new PlanValidator(), _store);
        _codingAgentService = new CodingAgentService(_registry, _taskService, _store, _promptValidator, taskRegistry: _taskRegistry, humanApprovalService: _humanApprovalService, logService: _logService);
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

    private static async Task<McpClient> CreateClientAsync(McpServer server, string? token = "test-token-mcp-phase08")
    {
        var httpClient = new HttpClient();
        if (!string.IsNullOrEmpty(token))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(server.EndpointUrl)
        }, httpClient, loggerFactory: null, ownsHttpClient: true);

        return await McpClient.CreateAsync(transport);
    }

    private static async Task<CallToolResult> CallToolAsync(McpClient client, string name, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        return await client.CallToolAsync(name, arguments ?? new Dictionary<string, object?>());
    }

    private static string GetResultText(CallToolResult result)
    {
        if (result.Content != null && result.Content.Count > 0 && result.Content[0] is TextContentBlock tb)
        {
            return tb.Text ?? "";
        }
        return "";
    }

    [Fact]
    public async Task RealMcpClient_Connect_Initialize_ListTools_Assert15Tools()
    {
        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        bool started = await server.StartAsync();
        Assert.True(started);

        try
        {
            await using var client = await CreateClientAsync(server);

            var tools = await client.ListToolsAsync();
            Assert.Equal(15, tools.Count);

            var toolNames = tools.Select(t => t.Name).ToList();

            // Assert 15 required tools exist
            Assert.Contains("ping_bridge", toolNames);
            Assert.Contains("get_bridge_status", toolNames);
            Assert.Contains("list_projects", toolNames);
            Assert.Contains("get_project", toolNames);
            Assert.Contains("get_project_progress", toolNames);
            Assert.Contains("get_current_phase", toolNames);
            Assert.Contains("get_dispatchable_tasks", toolNames);
            Assert.Contains("get_task", toolNames);
            Assert.Contains("prepare_task_execution", toolNames);
            Assert.Contains("dispatch_task", toolNames);
            Assert.Contains("get_current_execution", toolNames);
            Assert.Contains("get_execution", toolNames);
            Assert.Contains("cancel_execution", toolNames);
            Assert.Contains("get_review_package", toolNames);
            Assert.Contains("get_git_evidence", toolNames);

            // Assert forbidden tools are ABSENT
            Assert.DoesNotContain("approve_execution", toolNames);
            Assert.DoesNotContain("approve_human_gate", toolNames);
            Assert.DoesNotContain("run_shell", toolNames);

            // Assert dispatch_task schema does NOT contain workspacePath
            var dispatchTool = tools.First(t => t.Name == "dispatch_task");
            string schemaJson = JsonSerializer.Serialize(dispatchTool.ProtocolTool.InputSchema);
            Assert.DoesNotContain("workspacePath", schemaJson);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RealMcpClient_Call_PingBridge_And_GetBridgeStatus()
    {
        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        await server.StartAsync();

        try
        {
            await using var client = await CreateClientAsync(server);

            // 1. ping_bridge
            var pingRes = await CallToolAsync(client, "ping_bridge");
            string pingText = GetResultText(pingRes);
            Assert.Contains("mcpReady", pingText);
            Assert.Contains("0.8.0", pingText);

            // 2. get_bridge_status
            var statusRes = await CallToolAsync(client, "get_bridge_status");
            string statusText = GetResultText(statusRes);
            Assert.Contains("restStatus", statusText);
            Assert.Contains("RUNNING", statusText);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RealMcpClient_AuthenticationEnforcement()
    {
        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        await server.StartAsync();

        try
        {
            // Unauthenticated client (no token)
            await using var unauthClient = await CreateClientAsync(server, token: null);

            // ping_bridge allowed without auth
            var pingRes = await CallToolAsync(unauthClient, "ping_bridge");
            Assert.Contains("mcpReady", GetResultText(pingRes));

            // get_bridge_status rejected without auth -> returns UNAUTHORIZED
            var unauthRes = await CallToolAsync(unauthClient, "get_bridge_status");
            string unauthText = GetResultText(unauthRes);
            Assert.Contains("UNAUTHORIZED", unauthText);

            // get_bridge_status with valid Bearer token -> ALLOWED
            await using var authClient = await CreateClientAsync(server, token: "test-token-mcp-phase08");
            var authRes = await CallToolAsync(authClient, "get_bridge_status");
            string authText = GetResultText(authRes);
            Assert.Contains("restStatus", authText);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RealMcpClient_FullExecutionLifecycle_HumanGate_Dispatch_ReviewPackage_GitEvidence_UTF8()
    {
        var plan = E2ETestProjectSetup.CreateMiniCalculatorPlan();
        plan.Phases[0].Status = PhaseStatus.Completed;
        await _store.SavePlanAsync(plan);

        var cfg = _configService.LoadConfig();
        cfg.ProjectWorkspaces[plan.ProjectId] = _testDir;
        _configService.SaveConfig(cfg);

        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        await server.StartAsync();

        try
        {
            await using var client = await CreateClientAsync(server);

            // 1. prepare_task_execution for task-B4 with Vietnamese instructions
            string vietnamesePrompt = "Kiểm tra tiếng Việt.\nNguyên nhân: cấu hình không đúng.\nCách khắc phục: sửa cấu hình và chạy lại.\nKhông thể chia cho số 0.\nHoàn thành thành công.\n🤖 🚀 ✅";
            var prepRes = await CallToolAsync(client, "prepare_task_execution", new Dictionary<string, object?>
            {
                { "projectId", plan.ProjectId },
                { "phaseId", "phase-B" },
                { "taskId", "task-B4" },
                { "externalInstructions", vietnamesePrompt }
            });

            string prepJson = GetResultText(prepRes);
            Assert.Contains("promptId", prepJson);
            using var pkgDoc = JsonDocument.Parse(prepJson);
            string genPrompt = pkgDoc.RootElement.GetProperty("generatedPrompt").GetString()!;

            Assert.Contains("Kiểm tra tiếng Việt.", genPrompt);
            Assert.Contains("Nguyên nhân: cấu hình không đúng.", genPrompt);
            Assert.Contains("Cách khắc phục: sửa cấu hình và chạy lại.", genPrompt);
            Assert.Contains("Không thể chia cho số 0.", genPrompt);
            Assert.Contains("Hoàn thành thành công.", genPrompt);
            Assert.Contains("🤖 🚀 ✅", genPrompt);
            Assert.DoesNotContain("NguyÃªn nhÃ¢n", prepJson);
            string promptId = pkgDoc.RootElement.GetProperty("promptId").GetString()!;

            // 2. Dispatch task before Human Gate approval -> BLOCKED
            var blockedRes = await CallToolAsync(client, "dispatch_task", new Dictionary<string, object?>
            {
                { "projectId", plan.ProjectId },
                { "phaseId", "phase-B" },
                { "taskId", "task-B4" },
                { "promptId", promptId }
            });

            string blockedText = GetResultText(blockedRes);
            Assert.Contains("HUMAN_APPROVAL_REQUIRED", blockedText);
            Assert.Equal(0, _fakeAgent.CallCount);

            // 3. Grant trusted local human approval
            await _humanApprovalService.ApproveAsync(plan.ProjectId, "phase-B", "task-B4", plan.Version);

            // 4. Dispatch task after Human Gate approval -> ALLOWED
            var dispatchRes = await CallToolAsync(client, "dispatch_task", new Dictionary<string, object?>
            {
                { "projectId", plan.ProjectId },
                { "phaseId", "phase-B" },
                { "taskId", "task-B4" },
                { "promptId", promptId }
            });

            string dispatchText = GetResultText(dispatchRes);
            Assert.Contains("executionId", dispatchText);
            Assert.Equal(1, _fakeAgent.CallCount);

            using var dispatchDoc = JsonDocument.Parse(dispatchText);
            string executionId = dispatchDoc.RootElement.GetProperty("executionId").GetString()!;

            // 5. Retrieve Review Package
            var revRes = await CallToolAsync(client, "get_review_package", new Dictionary<string, object?>
            {
                { "executionId", executionId }
            });

            string revText = GetResultText(revRes);
            Assert.Contains("reviewStatus", revText);
            Assert.Contains(executionId, revText);

            // Record Git Evidence for executionId in taskRegistry to simulate Git evidence generation
            _taskRegistry.RegisterTask(new AgentTask { Id = executionId });
            _taskRegistry.RecordGitEvidence(executionId, new GitEvidence
            {
                TaskId = "task-B4",
                IsRepository = true,
                Status = "Ready",
                Diff = "+ fake diff content for test"
            });

            // 6. Retrieve Git Evidence
            var gitRes = await CallToolAsync(client, "get_git_evidence", new Dictionary<string, object?>
            {
                { "executionId", executionId }
            });

            string gitText = GetResultText(gitRes);
            Assert.Contains("status", gitText);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RealAntigravityAcceptanceTest_Phase08()
    {
        var envService = new AntigravityEnvironmentService(_logService);
        var envInfo = envService.CurrentInfo;

        if (envInfo.InstallationState == CliInstallationState.NotInstalled)
        {
            _logService.LogWarning("Real Antigravity acceptance test SKIPPED: CLI not installed.");
            return;
        }

        string tempGitDir = Path.Combine(Path.GetTempPath(), "AIBridge_Phase08_RealGit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempGitDir);

        try
        {
            InitializeGitRepository(tempGitDir);

            var gitCmd = new GitCommandService(_logService);
            var gitEnvService = new GitEnvironmentService(_logService, gitCmd);
            var gitEvidenceService = new GitEvidenceService(_logService, gitEnvService, gitCmd);
            var taskService = new TaskService(_logService, _taskRegistry, gitEvidenceService, _configService);

            var runner = new AntigravityRunner(_logService, envService, timeoutMinutes: 5);
            var agentReg = new CodingAgentRegistry();
            agentReg.RegisterAgent(new AntigravityCodingAgent(taskService, runner, envService));

            var plan = E2ETestProjectSetup.CreateMiniCalculatorPlan();
            var store = new FileProjectPlanStore(tempGitDir);
            await store.SavePlanAsync(plan);

            var codingAgentService = new CodingAgentService(agentReg, taskService, store, _promptValidator, gitEvidenceService, _taskRegistry, _humanApprovalService, _logService);
            var brainService = new AIBrainService(_logService, _configService, new AIBrainProviderRegistry());
            var planningService = new PlanningService(brainService, new PlanValidator(), store);

            var cfg = _configService.LoadConfig();
            cfg.ProjectWorkspaces[plan.ProjectId] = tempGitDir;
            _configService.SaveConfig(cfg);

            using var server = new McpServer(_configService, _logService, taskService, planningService, _promptService, codingAgentService, _humanApprovalService, _taskRegistry);
            await server.StartAsync();

            await using var client = await CreateClientAsync(server);

            string targetFile = "phase08-mcp-antigravity-test.txt";
            string prompt = $"Create file '{targetFile}' with exact content:\n" +
                            "Phase 08 MCP hoạt động.\n" +
                            "Kiểm tra tiếng Việt.\n" +
                            "Hoàn thành thành công.\n" +
                            "🤖 🚀 ✅";

            var prepRes = await CallToolAsync(client, "prepare_task_execution", new Dictionary<string, object?>
            {
                { "projectId", plan.ProjectId },
                { "phaseId", "phase-A" },
                { "taskId", "task-A1" },
                { "externalInstructions", prompt }
            });

            string prepContent = GetResultText(prepRes);
            using var pkgDoc = JsonDocument.Parse(prepContent);
            string promptId = pkgDoc.RootElement.GetProperty("promptId").GetString()!;

            // Dispatch task via MCP client
            var dispatchRes = await CallToolAsync(client, "dispatch_task", new Dictionary<string, object?>
            {
                { "projectId", plan.ProjectId },
                { "phaseId", "phase-A" },
                { "taskId", "task-A1" },
                { "promptId", promptId }
            });

            string dispatchContent = GetResultText(dispatchRes);
            using var resObj = JsonDocument.Parse(dispatchContent);
            string executionId = resObj.RootElement.GetProperty("executionId").GetString()!;

            // Verify review package via MCP client
            var revRes = await CallToolAsync(client, "get_review_package", new Dictionary<string, object?>
            {
                { "executionId", executionId }
            });
            string revContent = GetResultText(revRes);
            Assert.Contains("reviewStatus", revContent);

            // Verify file content & UTF-8 if generated by agent
            string createdFilePath = Path.Combine(tempGitDir, targetFile);
            if (File.Exists(createdFilePath))
            {
                string content = File.ReadAllText(createdFilePath, Encoding.UTF8);
                Assert.Contains("Phase 08 MCP hoạt động.", content);
                Assert.Contains("Kiểm tra tiếng Việt.", content);
                Assert.Contains("Hoàn thành thành công.", content);
                Assert.Contains("🤖 🚀 ✅", content);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempGitDir))
                {
                    Directory.Delete(tempGitDir, recursive: true);
                }
            }
            catch { }
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
            File.WriteAllText(readmePath, "# Phase 08 Test Repo\n", Encoding.UTF8);

            RunGitCommand(dir, "add README.md");
            RunGitCommand(dir, "commit -m \"initial commit\"");
        }
        catch { }
    }

    private static void RunGitCommand(string dir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        using var p = System.Diagnostics.Process.Start(psi);
        p?.WaitForExit(5000);
    }
}
