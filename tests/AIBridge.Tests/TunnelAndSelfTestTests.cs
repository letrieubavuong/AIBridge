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

    [Fact]
    public void Token_NeverAppearsInMcpEndpointQueryString_Or_TunnelCommand()
    {
        string baseEndpoint = "http://127.0.0.1:8788/mcp";
        string tunnelArgs = CloudflareTunnelService.BuildTunnelArguments(baseEndpoint);
        string publicEndpoint = CloudflareTunnelService.FormatPublicMcpEndpoint("https://my-tunnel.trycloudflare.com");

        Assert.DoesNotContain("token=", baseEndpoint);
        Assert.DoesNotContain("?token", baseEndpoint);
        Assert.DoesNotContain("token=", tunnelArgs);
        Assert.DoesNotContain("?token", tunnelArgs);
        Assert.DoesNotContain("token=", publicEndpoint);
        Assert.DoesNotContain("?token", publicEndpoint);
    }

    [Fact]
    public void NormalizeTargetUrl_EmptyOrNullTarget_ThrowsArgumentException_DoesNotFallbackTo8799()
    {
        var exEmpty = Assert.Throws<ArgumentException>(() => CloudflareTunnelService.NormalizeTargetUrl(""));
        var exNull = Assert.Throws<ArgumentException>(() => CloudflareTunnelService.NormalizeTargetUrl(null!));
        var exWhitespace = Assert.Throws<ArgumentException>(() => CloudflareTunnelService.NormalizeTargetUrl("   "));

        Assert.Contains("cannot be null or empty", exEmpty.Message);
        Assert.Contains("cannot be null or empty", exNull.Message);
        Assert.Contains("cannot be null or empty", exWhitespace.Message);
    }

    [Fact]
    public void NormalizeTargetUrl_MalformedOrNonLoopback_FailsSafely()
    {
        var exMalformed = Assert.Throws<ArgumentException>(() => CloudflareTunnelService.NormalizeTargetUrl("not-a-url"));
        var exNonLoopback = Assert.Throws<ArgumentException>(() => CloudflareTunnelService.NormalizeTargetUrl("http://google.com:8788/mcp"));
        var exNonHttp = Assert.Throws<ArgumentException>(() => CloudflareTunnelService.NormalizeTargetUrl("ftp://127.0.0.1:8788/mcp"));

        Assert.Contains("malformed", exMalformed.Message);
        Assert.Contains("loopback", exNonLoopback.Message);
        Assert.Contains("http or https", exNonHttp.Message);
    }

    [Fact]
    public void NormalizeTargetUrl_LocalBridgePort9889_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => CloudflareTunnelService.NormalizeTargetUrl("http://127.0.0.1:9889/mcp"));
        Assert.Contains("Local Bridge port 9889 must NEVER be used", ex.Message);
    }

    [Fact]
    public void NormalizeTargetUrl_DerivesFromAuthoritativeTargetPort()
    {
        string result = CloudflareTunnelService.NormalizeTargetUrl("http://127.0.0.1:8999/mcp");
        Assert.Equal("http://127.0.0.1:8999", result);
    }

    [Fact]
    public void DownloadAllowlist_ValidatesOfficialDomains_RejectsUntrusted()
    {
        Assert.True(CloudflareTunnelService.IsAllowedDownloadHost("github.com"));
        Assert.True(CloudflareTunnelService.IsAllowedDownloadHost("api.github.com"));
        Assert.True(CloudflareTunnelService.IsAllowedDownloadHost("github-releases.githubusercontent.com"));
        Assert.True(CloudflareTunnelService.IsAllowedDownloadHost("release-assets.githubusercontent.com"));
        Assert.True(CloudflareTunnelService.IsAllowedDownloadHost("objects.githubusercontent.com"));
        Assert.True(CloudflareTunnelService.IsAllowedDownloadHost("cloudflare.com"));
        Assert.True(CloudflareTunnelService.IsAllowedDownloadHost("downloads.cloudflare.com"));

        Assert.False(CloudflareTunnelService.IsAllowedDownloadHost("malicious-site.com"));
        Assert.False(CloudflareTunnelService.IsAllowedDownloadHost("github.com.attacker.com"));
        Assert.False(CloudflareTunnelService.IsAllowedDownloadHost("github.com.attacker.example"));
        Assert.False(CloudflareTunnelService.IsAllowedDownloadHost("release-assets.githubusercontent.com.attacker.example"));
        Assert.False(CloudflareTunnelService.IsAllowedDownloadHost("fakecloudflare.com"));
    }

    [Fact]
    public void CheckInstallationAsync_DoesNotInstallAutomatically()
    {
        var logService = new LogService();
        var configService = new ConfigService(logService, Path.Combine(_testDir, "test_config_noinst.json"));
        var cfg = configService.LoadConfig();
        cfg.CloudflaredPath = Path.Combine(_testDir, "non_existent_cloudflared.exe");
        configService.SaveConfig(cfg);

        var tunnelService = new CloudflareTunnelService(configService, logService);
        var info = tunnelService.GetInfo();

        Assert.False(tunnelService.IsInstalled);
        Assert.Equal(TunnelStatus.NotInstalled, info.Status);
        Assert.False(File.Exists(cfg.CloudflaredPath));
    }

    [Fact]
    public void Exactly15Phase08McpTools_Preserved_NoHumanGate_NoShell()
    {
        Assert.Equal(15, McpSelfTestService.Expected15Tools.Length);

        string[] expected = new[]
        {
            "ping_bridge",
            "get_bridge_status",
            "list_projects",
            "get_project",
            "get_project_progress",
            "get_current_phase",
            "get_dispatchable_tasks",
            "get_task",
            "prepare_task_execution",
            "dispatch_task",
            "get_current_execution",
            "get_execution",
            "cancel_execution",
            "get_review_package",
            "get_git_evidence"
        };

        foreach (var tool in expected)
        {
            Assert.Contains(tool, McpSelfTestService.Expected15Tools);
        }

        Assert.DoesNotContain(McpSelfTestService.Expected15Tools, t => t.Contains("approve", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(McpSelfTestService.Expected15Tools, t => t.Contains("human", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(McpSelfTestService.Expected15Tools, t => t.Equals("shell", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(McpSelfTestService.Expected15Tools, t => t.Equals("cmd", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(McpSelfTestService.Expected15Tools, t => t.Equals("write_file", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(McpSelfTestService.Expected15Tools, t => t.Equals("read_file", StringComparison.OrdinalIgnoreCase));
    }
}
