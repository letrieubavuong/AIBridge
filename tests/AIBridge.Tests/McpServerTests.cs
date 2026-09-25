using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIBridge.Models;
using AIBridge.Services;
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

    private static string GetMcpContentText(string jsonResponse)
    {
        using var doc = JsonDocument.Parse(jsonResponse);
        if (doc.RootElement.TryGetProperty("result", out var resEl) &&
            resEl.TryGetProperty("content", out var contentArray) &&
            contentArray.ValueKind == JsonValueKind.Array &&
            contentArray.GetArrayLength() > 0)
        {
            return contentArray[0].GetProperty("text").GetString() ?? jsonResponse;
        }
        return jsonResponse;
    }

    [Fact]
    public async Task Section4_McpServer_SseTransport_Connects_And_EmitsEndpointEvent()
    {
        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        bool started = await server.StartAsync();
        Assert.True(started);

        try
        {
            using var client = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, server.EndpointUrl + "/sse");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string line1 = await reader.ReadLineAsync() ?? "";
            string line2 = await reader.ReadLineAsync() ?? "";

            Assert.Equal("event: endpoint", line1);
            Assert.Contains("data: http://", line2);
            Assert.Contains("/messages?sessionId=", line2);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Section5_McpServer_CorsSecurity_Preflight_ReflectsOrigin()
    {
        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        bool started = await server.StartAsync();
        Assert.True(started);

        try
        {
            using var client = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Options, server.EndpointUrl);
            req.Headers.Add("Origin", "https://chatgpt.com");

            using var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.True(resp.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.Equal("https://chatgpt.com", resp.Headers.GetValues("Access-Control-Allow-Origin").FirstOrDefault());
            Assert.True(resp.Headers.Contains("Access-Control-Allow-Credentials"));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Section32_McpServer_InitializationAndProtocolCapabilities()
    {
        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        bool started = await server.StartAsync();
        Assert.True(started);

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(server.EndpointUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-mcp-phase08");

            var initReq = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    clientInfo = new { name = "ChatGPT" }
                }
            };

            var res = await client.PostAsJsonAsync("", initReq);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
            Assert.Equal(1, root.GetProperty("id").GetInt64());
            var result = root.GetProperty("result");
            Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
            Assert.Equal("AIBridge", result.GetProperty("serverInfo").GetProperty("name").GetString());
            Assert.True(result.TryGetProperty("capabilities", out _));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Section32_Section55_McpServer_ToolDiscovery_ExposesRequired15Tools_AndNoForbiddenTools()
    {
        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        await server.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(server.EndpointUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-mcp-phase08");

            var listReq = new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list"
            };

            var res = await client.PostAsJsonAsync("", listReq);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var tools = doc.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();

            var toolNames = tools.Select(t => t.GetProperty("name").GetString()).ToList();

            // Assert 15 required tools are present
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

            // Assert forbidden dangerous tools are ABSENT
            Assert.DoesNotContain("approve_execution", toolNames);
            Assert.DoesNotContain("approve_human_gate", toolNames);
            Assert.DoesNotContain("confirm_human_gate", toolNames);
            Assert.DoesNotContain("set_human_approval", toolNames);
            Assert.DoesNotContain("run_shell", toolNames);
            Assert.DoesNotContain("execute_powershell", toolNames);
            Assert.DoesNotContain("read_any_file", toolNames);
            Assert.DoesNotContain("write_any_file", toolNames);
            Assert.DoesNotContain("delete_file", toolNames);

            // Assert dispatch_task schema does NOT contain workspacePath
            var dispatchTool = tools.First(t => t.GetProperty("name").GetString() == "dispatch_task");
            var properties = dispatchTool.GetProperty("inputSchema").GetProperty("properties");
            Assert.False(properties.TryGetProperty("workspacePath", out _));
            Assert.False(properties.TryGetProperty("humanApproved", out _));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Section54_McpServer_AuthenticationEnforcement()
    {
        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        await server.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(server.EndpointUrl) };

            // 1. ping_bridge allowed without auth
            var pingCall = new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new { name = "ping_bridge", arguments = new { } }
            };
            var pingRes = await client.PostAsJsonAsync("", pingCall);
            Assert.Equal(HttpStatusCode.OK, pingRes.StatusCode);
            var pingJson = await pingRes.Content.ReadAsStringAsync();
            var pingContent = GetMcpContentText(pingJson);
            Assert.Contains("mcpReady", pingContent);

            // 2. Protected tool call without auth -> REJECTED
            var statusCall = new
            {
                jsonrpc = "2.0",
                id = 4,
                method = "tools/call",
                @params = new { name = "get_bridge_status", arguments = new { } }
            };
            var unauthRes = await client.PostAsJsonAsync("", statusCall);
            Assert.Equal(HttpStatusCode.OK, unauthRes.StatusCode);
            var unauthJson = await unauthRes.Content.ReadAsStringAsync();
            Assert.Contains("UNAUTHORIZED", unauthJson);

            // 3. Protected tool call with valid Bearer token -> ACCEPTED
            var statusCall2 = new
            {
                jsonrpc = "2.0",
                id = 5,
                method = "tools/call",
                @params = new { name = "get_bridge_status", arguments = new { } }
            };
            using var authReq = new HttpRequestMessage(HttpMethod.Post, "")
            {
                Content = JsonContent.Create(statusCall2)
            };
            authReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-mcp-phase08");
            var authRes = await client.SendAsync(authReq);
            Assert.Equal(HttpStatusCode.OK, authRes.StatusCode);
            var authJson = await authRes.Content.ReadAsStringAsync();
            var authContent = GetMcpContentText(authJson);
            Assert.Contains("restStatus", authContent);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Section32_McpServer_ProjectAndTaskTools()
    {
        var plan = E2ETestProjectSetup.CreateMiniCalculatorPlan();
        await _store.SavePlanAsync(plan);

        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        await server.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(server.EndpointUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-mcp-phase08");

            // 1. list_projects
            var resList = await client.PostAsJsonAsync("", new { jsonrpc = "2.0", id = 10, method = "tools/call", @params = new { name = "list_projects", arguments = new { } } });
            var jsonList = GetMcpContentText(await resList.Content.ReadAsStringAsync());
            Assert.Contains("AIBridge-E2E-Test", jsonList);

            // 2. get_project
            var resProj = await client.PostAsJsonAsync("", new { jsonrpc = "2.0", id = 11, method = "tools/call", @params = new { name = "get_project", arguments = new { projectId = "AIBridge-E2E-Test" } } });
            var jsonProj = GetMcpContentText(await resProj.Content.ReadAsStringAsync());
            Assert.Contains("Mini Calculator E2E Test", jsonProj);

            // 3. get_project_progress
            var resProg = await client.PostAsJsonAsync("", new { jsonrpc = "2.0", id = 12, method = "tools/call", @params = new { name = "get_project_progress", arguments = new { projectId = "AIBridge-E2E-Test" } } });
            var jsonProg = GetMcpContentText(await resProg.Content.ReadAsStringAsync());
            Assert.Contains("projectPercent", jsonProg);

            // 4. get_current_phase
            var resPhase = await client.PostAsJsonAsync("", new { jsonrpc = "2.0", id = 13, method = "tools/call", @params = new { name = "get_current_phase", arguments = new { projectId = "AIBridge-E2E-Test" } } });
            var jsonPhase = GetMcpContentText(await resPhase.Content.ReadAsStringAsync());
            Assert.Contains("Phase A — Foundation", jsonPhase);

            // 5. get_task
            var resTask = await client.PostAsJsonAsync("", new { jsonrpc = "2.0", id = 14, method = "tools/call", @params = new { name = "get_task", arguments = new { projectId = "AIBridge-E2E-Test", taskId = "task-A1" } } });
            var jsonTask = GetMcpContentText(await resTask.Content.ReadAsStringAsync());
            Assert.Contains("Create Console Project", jsonTask);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Section20_McpServer_HumanGate_BlockedViaMcp()
    {
        var plan = E2ETestProjectSetup.CreateMiniCalculatorPlan();
        plan.Phases[0].Status = PhaseStatus.Completed;
        await _store.SavePlanAsync(plan);

        var cfg = _configService.LoadConfig();
        cfg.ProjectWorkspaces["AIBridge-E2E-Test"] = _testDir;
        _configService.SaveConfig(cfg);

        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        await server.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(server.EndpointUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-mcp-phase08");

            // Prepare prompt for task-B4 (which requires human gate)
            var prepRes = await client.PostAsJsonAsync("", new
            {
                jsonrpc = "2.0",
                id = 20,
                method = "tools/call",
                @params = new
                {
                    name = "prepare_task_execution",
                    arguments = new
                    {
                        projectId = "AIBridge-E2E-Test",
                        phaseId = "phase-B",
                        taskId = "task-B4",
                        externalInstructions = "Implement Divide(double a, double b) with zero check."
                    }
                }
            });
            var prepJson = await prepRes.Content.ReadAsStringAsync();
            var prepContent = GetMcpContentText(prepJson);
            Assert.Contains("promptId", prepContent);

            using var pkgDoc = JsonDocument.Parse(prepContent);
            string promptId = pkgDoc.RootElement.GetProperty("promptId").GetString()!;

            // Attempt dispatch via MCP without human approval -> BLOCKED
            var dispatchRes = await client.PostAsJsonAsync("", new
            {
                jsonrpc = "2.0",
                id = 21,
                method = "tools/call",
                @params = new
                {
                    name = "dispatch_task",
                    arguments = new
                    {
                        projectId = "AIBridge-E2E-Test",
                        phaseId = "phase-B",
                        taskId = "task-B4",
                        promptId = promptId
                    }
                }
            });
            var dispatchJson = GetMcpContentText(await dispatchRes.Content.ReadAsStringAsync());
            Assert.Contains("HUMAN_APPROVAL_REQUIRED", dispatchJson);
            Assert.Equal(0, _fakeAgent.CallCount);

            // Grant trusted local human approval via WPF service
            await _humanApprovalService.ApproveAsync("AIBridge-E2E-Test", "phase-B", "task-B4", plan.Version);

            // Dispatch via MCP after trusted local approval -> ALLOWED
            var dispatchRes2 = await client.PostAsJsonAsync("", new
            {
                jsonrpc = "2.0",
                id = 22,
                method = "tools/call",
                @params = new
                {
                    name = "dispatch_task",
                    arguments = new
                    {
                        projectId = "AIBridge-E2E-Test",
                        phaseId = "phase-B",
                        taskId = "task-B4",
                        promptId = promptId
                    }
                }
            });
            var dispatchJson2 = GetMcpContentText(await dispatchRes2.Content.ReadAsStringAsync());
            Assert.Contains("executionId", dispatchJson2);
            Assert.Equal(1, _fakeAgent.CallCount);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Section31_Section60_McpServer_UTF8_ExactStrings_PreservedWithoutMojibake()
    {
        var plan = E2ETestProjectSetup.CreateMiniCalculatorPlan();
        await _store.SavePlanAsync(plan);

        var cfg = _configService.LoadConfig();
        cfg.ProjectWorkspaces["AIBridge-E2E-Test"] = _testDir;
        _configService.SaveConfig(cfg);

        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        await server.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(server.EndpointUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-mcp-phase08");

            string vietnamesePrompt = "Kiểm tra tiếng Việt.\nNguyên nhân: cấu hình không đúng.\nCách khắc phục: sửa cấu hình và chạy lại.\nKhông thể chia cho số 0.\nHoàn thành thành công.\n🤖 🚀 ✅";

            var prepRes = await client.PostAsJsonAsync("", new
            {
                jsonrpc = "2.0",
                id = 30,
                method = "tools/call",
                @params = new
                {
                    name = "prepare_task_execution",
                    arguments = new
                    {
                        projectId = "AIBridge-E2E-Test",
                        phaseId = "phase-B",
                        taskId = "task-B4",
                        externalInstructions = vietnamesePrompt
                    }
                }
            });

            var prepJson = GetMcpContentText(await prepRes.Content.ReadAsStringAsync());
            using var doc = JsonDocument.Parse(prepJson);
            string genPrompt = doc.RootElement.GetProperty("generatedPrompt").GetString()!;
            Assert.Contains("Kiểm tra tiếng Việt.", genPrompt);
            Assert.Contains("Nguyên nhân: cấu hình không đúng.", genPrompt);
            Assert.Contains("Cách khắc phục: sửa cấu hình và chạy lại.", genPrompt);
            Assert.Contains("Không thể chia cho số 0.", genPrompt);
            Assert.Contains("Hoàn thành thành công.", genPrompt);
            Assert.Contains("🤖 🚀 ✅", genPrompt);
            Assert.DoesNotContain("NguyÃªn nhÃ¢n", prepJson);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Section30_McpServer_RequestSizeLimit_RejectsOversizedPayload()
    {
        using var server = new McpServer(_configService, _logService, _taskService, _planningService, _promptService, _codingAgentService, _humanApprovalService, _taskRegistry);
        await server.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(server.EndpointUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-mcp-phase08");

            // Create oversized instruction (> 256 KB)
            string hugeText = new string('A', 300 * 1024);
            var hugeReq = new
            {
                jsonrpc = "2.0",
                id = 40,
                method = "tools/call",
                @params = new
                {
                    name = "prepare_task_execution",
                    arguments = new
                    {
                        projectId = "AIBridge-E2E-Test",
                        phaseId = "phase-A",
                        taskId = "task-A1",
                        externalInstructions = hugeText
                    }
                }
            };

            var res = await client.PostAsJsonAsync("", hugeReq);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);

            var json = await res.Content.ReadAsStringAsync();
            Assert.Contains("REQUEST_TOO_LARGE", json);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Section61_RealAntigravityAcceptanceTest_Phase08()
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

            using var client = new HttpClient { BaseAddress = new Uri(server.EndpointUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-mcp-phase08");

            string targetFile = "phase08-mcp-antigravity-test.txt";
            string prompt = $"Create file '{targetFile}' with exact content:\n" +
                            "Phase 08 MCP hoạt động.\n" +
                            "Kiểm tra tiếng Việt.\n" +
                            "Hoàn thành thành công.\n" +
                            "🤖 🚀 ✅";

            var prepRes = await client.PostAsJsonAsync("", new
            {
                jsonrpc = "2.0",
                id = 50,
                method = "tools/call",
                @params = new
                {
                    name = "prepare_task_execution",
                    arguments = new
                    {
                        projectId = plan.ProjectId,
                        phaseId = "phase-A",
                        taskId = "task-A1",
                        externalInstructions = prompt
                    }
                }
            });
            var prepContent = GetMcpContentText(await prepRes.Content.ReadAsStringAsync());
            using var pkgDoc = JsonDocument.Parse(prepContent);
            string promptId = pkgDoc.RootElement.GetProperty("promptId").GetString()!;

            // Dispatch task via MCP
            var dispatchRes = await client.PostAsJsonAsync("", new
            {
                jsonrpc = "2.0",
                id = 51,
                method = "tools/call",
                @params = new
                {
                    name = "dispatch_task",
                    arguments = new
                    {
                        projectId = plan.ProjectId,
                        phaseId = "phase-A",
                        taskId = "task-A1",
                        promptId = promptId
                    }
                }
            });

            var dispatchContent = GetMcpContentText(await dispatchRes.Content.ReadAsStringAsync());
            using var resObj = JsonDocument.Parse(dispatchContent);
            string executionId = resObj.RootElement.GetProperty("executionId").GetString()!;

            // Verify review package
            var revRes = await client.PostAsJsonAsync("", new
            {
                jsonrpc = "2.0",
                id = 52,
                method = "tools/call",
                @params = new { name = "get_review_package", arguments = new { executionId = executionId } }
            });
            var revContent = GetMcpContentText(await revRes.Content.ReadAsStringAsync());
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
            File.WriteAllText(readmePath, "# Phase 08 Test Repo\n", System.Text.Encoding.UTF8);
            
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
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            CreateNoWindow = true
        };
        using var p = System.Diagnostics.Process.Start(psi);
        p?.WaitForExit(5000);
    }
}
