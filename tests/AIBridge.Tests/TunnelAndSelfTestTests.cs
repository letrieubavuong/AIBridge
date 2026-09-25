using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIBridge.Infrastructure;
using AIBridge.Models;
using AIBridge.Services;
using AIBridge.ViewModels;
using AIBridge.Views;
using Xunit;

namespace AIBridge.Tests;

public class TunnelAndSelfTestTests : IDisposable
{
    private readonly string _testDir;

    public TunnelAndSelfTestTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AIBridge_TunnelTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
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
    public void PublicUrlParsing_ValidTryCloudflareUrls_ExtractedSuccessfully()
    {
        string log1 = "2026-09-25T10:00:00Z INF +----------------------------------------------------------------------------------+";
        string log2 = "2026-09-25T10:00:00Z INF |  Your quick tunnel has been created! Visit it at:                               |";
        string log3 = "2026-09-25T10:00:00Z INF |  https://my-demo-app.trycloudflare.com                                            |";

        Assert.Null(CloudflareTunnelService.ParsePublicUrlFromOutput(log1));
        Assert.Null(CloudflareTunnelService.ParsePublicUrlFromOutput(log2));
        Assert.Equal("https://my-demo-app.trycloudflare.com", CloudflareTunnelService.ParsePublicUrlFromOutput(log3));
    }

    [Fact]
    public void PublicUrlParsing_InvalidOrHttpUrls_Rejected()
    {
        Assert.Null(CloudflareTunnelService.ParsePublicUrlFromOutput("http://my-demo-app.trycloudflare.com"));
        Assert.Null(CloudflareTunnelService.ParsePublicUrlFromOutput("https://google.com"));
        Assert.Null(CloudflareTunnelService.ParsePublicUrlFromOutput("Random stdout noise"));
    }

    [Fact]
    public void FormatPublicMcpEndpoint_AppendsMcpPathCorrectly()
    {
        string baseUrl = "https://example-tunnel.trycloudflare.com";
        Assert.Equal("https://example-tunnel.trycloudflare.com/mcp", CloudflareTunnelService.FormatPublicMcpEndpoint(baseUrl));

        string alreadyMcp = "https://example-tunnel.trycloudflare.com/mcp";
        Assert.Equal("https://example-tunnel.trycloudflare.com/mcp", CloudflareTunnelService.FormatPublicMcpEndpoint(alreadyMcp));
    }

    [Fact]
    public void NormalizeTargetUrl_StripsPathAndFormatsBaseUrl()
    {
        Assert.Equal("http://127.0.0.1:8799", CloudflareTunnelService.NormalizeTargetUrl("http://127.0.0.1:8799/mcp"));
        Assert.Equal("http://127.0.0.1:8820", CloudflareTunnelService.NormalizeTargetUrl("http://127.0.0.1:8820"));
    }

    [Fact]
    public void BuildTunnelArguments_UsesAuthoritativeTargetPort()
    {
        string targetUrl = "http://127.0.0.1:8799/mcp";
        string args = CloudflareTunnelService.BuildTunnelArguments(targetUrl);

        Assert.Equal("tunnel --url http://127.0.0.1:8799", args);
        Assert.DoesNotContain("8788", args);
    }

    [Fact]
    public void CloudflaredDetection_ExecutableResolution_ChecksConfigAndPortablePath()
    {
        var logService = new LogService();
        var configService = new ConfigService(logService, Path.Combine(_testDir, "test_config.json"));

        var tunnelService = new CloudflareTunnelService(configService, logService);
        string path = tunnelService.ResolveExecutablePath();

        Assert.NotNull(path);
        Assert.NotEmpty(path);
    }

    [Fact]
    public async Task McpSelfTest_LocalMcpServer_Verifies15Tools_And_Ping()
    {
        var logService = new LogService();
        var configService = new ConfigService(logService, Path.Combine(_testDir, "test_config.json"));
        var cfg = configService.LoadConfig();
        cfg.McpHost = "127.0.0.1";
        cfg.McpPort = 8835;
        cfg.ApiToken = "test-token-selftest";
        cfg.WorkspacePath = _testDir;
        configService.SaveConfig(cfg);

        var taskRegistry = new TaskRegistry();
        var taskService = new TaskService(logService, taskRegistry);
        var store = new FileProjectPlanStore(_testDir);
        var brainService = new AIBrainService(logService, configService, new AIBrainProviderRegistry());
        var planningService = new PlanningService(brainService, new PlanValidator(), store);
        var promptService = new ExecutionPromptService(new PromptValidator(), logService: logService);
        var codingAgentService = new CodingAgentService(new CodingAgentRegistry(), taskService, store, new PromptValidator(), taskRegistry: taskRegistry, logService: logService);
        var humanApprovalService = new HumanApprovalService();

        using var server = new McpServer(configService, logService, taskService, planningService, promptService, codingAgentService, humanApprovalService, taskRegistry);
        await server.StartAsync();

        try
        {
            var selfTestService = new McpSelfTestService(logService);
            var result = await selfTestService.RunSelfTestAsync(server.EndpointUrl, cfg.ApiToken);

            Assert.True(result.ServerRunning);
            Assert.True(result.InitializePassed);
            Assert.True(result.ToolDiscoveryPassed);
            Assert.Equal(15, result.DiscoveredToolCount);
            Assert.True(result.PingPassed);
            Assert.True(result.AuthenticationPassed);
            Assert.True(result.OverallPassed);
            Assert.Empty(result.Errors);

            // Assert 15 tools contain no generic shell or human gate approval tools
            Assert.DoesNotContain(result.DiscoveredTools, t => t.Contains("approve", StringComparison.OrdinalIgnoreCase) || t.Contains("human", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.DiscoveredTools, t => t.Equals("shell", StringComparison.OrdinalIgnoreCase) || t.Equals("cmd", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public void UI_TruthfulStatusLabels_DoesNotFalselyClaimChatGPTConnected()
    {
        var logService = new LogService();
        var configService = new ConfigService(logService, Path.Combine(_testDir, "test_config.json"));
        var taskRegistry = new TaskRegistry();
        var taskService = new TaskService(logService, taskRegistry);
        var envService = new AntigravityEnvironmentService(logService);
        var runner = new AntigravityRunner(logService, envService);
        var bridgeServer = new BridgeServer(configService, logService, taskService, taskRegistry, envService, runner);

        var vm = new MainViewModel(configService, logService, taskService, envService, runner, bridgeServer);

        // When MCP is stopped
        Assert.Equal("MCP cục bộ: Đã tắt", vm.ChatGptStatusVietnamese);
        Assert.DoesNotContain("Đã kết nối", vm.ChatGptStatusVietnamese);

        // Simulated MCP running
        vm.IsMcpRunning = true;
        Assert.Equal("MCP cục bộ: Đang hoạt động", vm.ChatGptStatusVietnamese);
        Assert.DoesNotContain("ChatGPT đã kết nối", vm.ChatGptStatusVietnamese);
    }
}
