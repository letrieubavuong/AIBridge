using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIBridge.Infrastructure;
using AIBridge.Models;
using AIBridge.Services;
using AIBridge.ViewModels;
using ModelContextProtocol.Client;
using Xunit;

namespace AIBridge.Tests;

public class Phase08UiAndRuntimeTests : IDisposable
{
    private readonly string _testDir;

    public Phase08UiAndRuntimeTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AIBridge_Phase08UiTest_" + Guid.NewGuid().ToString("N"));
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
    public async Task McpServer_StartStop_CanExecute_And_State_Test()
    {
        var logService = new LogService();
        var configService = new ConfigService(logService, Path.Combine(_testDir, "test_config.json"));
        var cfg = configService.LoadConfig();
        cfg.McpHost = "127.0.0.1";
        cfg.McpPort = 8798;
        cfg.WorkspacePath = _testDir;
        configService.SaveConfig(cfg);

        var taskRegistry = new TaskRegistry();
        var taskService = new TaskService(logService, taskRegistry);
        var envService = new AntigravityEnvironmentService(logService);
        var runner = new AntigravityRunner(logService, envService);
        var bridgeServer = new BridgeServer(configService, logService, taskService, taskRegistry, envService, runner);

        var vm = new MainViewModel(configService, logService, taskService, envService, runner, bridgeServer);

        Assert.False(vm.IsMcpRunning);
        Assert.True(vm.StartMcpCommand.CanExecute(null));
        Assert.False(vm.StopMcpCommand.CanExecute(null));

        await ((AsyncRelayCommand)vm.StartMcpCommand).ExecuteAsync(null);

        Assert.True(vm.IsMcpRunning);
        Assert.False(vm.StartMcpCommand.CanExecute(null));
        Assert.True(vm.StopMcpCommand.CanExecute(null));

        await ((AsyncRelayCommand)vm.StopMcpCommand).ExecuteAsync(null);

        Assert.False(vm.IsMcpRunning);
        Assert.True(vm.StartMcpCommand.CanExecute(null));
        Assert.False(vm.StopMcpCommand.CanExecute(null));
    }

    [Fact]
    public async Task ProcessStartInfo_ReadOnlyConfiguration_DoesNotThrowStandardInputEncodingException()
    {
        var logService = new LogService();
        var envService = new AntigravityEnvironmentService(logService);
        var resolvedCli = envService.ResolveCliExecutable();

        if (string.IsNullOrWhiteSpace(resolvedCli))
        {
            return;
        }

        var method = typeof(AntigravityEnvironmentService).GetMethod(
            "CreateCliStartInfo",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var psi = (System.Diagnostics.ProcessStartInfo)method!.Invoke(null, new object[] { resolvedCli, "--version" })!;

        Assert.False(psi.RedirectStandardInput);

        // Verify starting process with this ProcessStartInfo does NOT throw InvalidOperationException regarding StandardInputEncoding
        using var process = new System.Diagnostics.Process { StartInfo = psi };
        var exception = Record.Exception(() => process.Start());
        Assert.Null(exception);

        await process.WaitForExitAsync();
    }

    [Fact]
    public async Task AntigravityDetection_RealExecutable_VerifiesSuccessfully_WhenPresent()
    {
        var logService = new LogService();
        var envService = new AntigravityEnvironmentService(logService);
        var resolvedCli = envService.ResolveCliExecutable();

        if (string.IsNullOrWhiteSpace(resolvedCli))
        {
            return;
        }

        var info = await envService.DetectAndVerifyEnvironmentAsync(resolvedCli);
        Assert.Equal(CliInstallationState.Ready, info.InstallationState);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.NotEqual(CliAuthState.Error, info.AuthState);
    }

    [Fact]
    public async Task AntigravityDetection_ExplicitPath_NotExist_ReturnsNotFound()
    {
        var logService = new LogService();
        var envService = new AntigravityEnvironmentService(logService);

        string nonExistentPath = Path.Combine(_testDir, "agy_fake_nonexistent.exe");
        var info = await envService.DetectAndVerifyEnvironmentAsync(nonExistentPath);

        Assert.Equal(CliInstallationState.NotInstalled, info.InstallationState);
        Assert.Contains("File not found", info.StatusMessage);
    }

    [Fact]
    public async Task AntigravityDetection_CliNotFound_Vs_AuthState_Mapping()
    {
        var logService = new LogService();
        var configService = new ConfigService(logService, Path.Combine(_testDir, "test_config.json"));
        var taskRegistry = new TaskRegistry();
        var taskService = new TaskService(logService, taskRegistry);
        var envService = new AntigravityEnvironmentService(logService);
        var runner = new AntigravityRunner(logService, envService);
        var bridgeServer = new BridgeServer(configService, logService, taskService, taskRegistry, envService, runner);

        var vm = new MainViewModel(configService, logService, taskService, envService, runner, bridgeServer);

        // Simulate NotInstalled state
        var notInstalledInfo = new AntigravityEnvironmentInfo
        {
            InstallationState = CliInstallationState.NotInstalled,
            AuthState = CliAuthState.Unknown
        };

        // Access private UpdateEnvironmentUI via reflection or refresh
        var method = typeof(MainViewModel).GetMethod("UpdateEnvironmentUI", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(vm, new object[] { notInstalledInfo });

        Assert.Equal("NOT FOUND", vm.CliStateText);
        Assert.Equal("NOT FOUND", vm.AntigravityStatus);
        Assert.Equal("CLI NOT FOUND", vm.AuthStateText);

        // Simulate Ready state
        var readyInfo = new AntigravityEnvironmentInfo
        {
            InstallationState = CliInstallationState.Ready,
            Version = "1.2.3",
            AuthState = CliAuthState.Ready
        };
        method?.Invoke(vm, new object[] { readyInfo });

        Assert.Equal("FOUND", vm.CliStateText);
        Assert.Equal("FOUND (1.2.3)", vm.AntigravityStatus);
        Assert.Equal("AUTHENTICATED", vm.AuthStateText);
    }

    [Fact]
    public void WorkspaceValidation_InvalidWorkspace_ReturnsInvalidStatus()
    {
        var logService = new LogService();
        var configService = new ConfigService(logService, Path.Combine(_testDir, "test_config.json"));
        var taskRegistry = new TaskRegistry();
        var taskService = new TaskService(logService, taskRegistry);
        var envService = new AntigravityEnvironmentService(logService);
        var runner = new AntigravityRunner(logService, envService);
        var bridgeServer = new BridgeServer(configService, logService, taskService, taskRegistry, envService, runner);

        string invalidPath = Path.Combine(_testDir, "NonExistentDirectory_" + Guid.NewGuid().ToString("N"));

        var vm = new MainViewModel(configService, logService, taskService, envService, runner, bridgeServer)
        {
            WorkspacePath = invalidPath
        };

        Assert.Equal("NOT FOUND / WORKSPACE INVALID", vm.WorkspaceStatus);
    }

    [Fact]
    public void TestConfigIsolation_ProductionConfigNotMutatedByTests()
    {
        var logService = new LogService();
        string userConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIBridge", "config.json");

        DateTime preTestWriteTime = File.Exists(userConfigPath) ? File.GetLastWriteTimeUtc(userConfigPath) : DateTime.MinValue;

        // Run isolated ConfigService
        string testConfigPath = Path.Combine(_testDir, "isolated_config.json");
        var testConfigService = new ConfigService(logService, testConfigPath);
        var cfg = testConfigService.LoadConfig();
        cfg.WorkspacePath = Path.Combine(_testDir, "AIBridge_McpServerTest_Fake");
        testConfigService.SaveConfig(cfg);

        Assert.True(File.Exists(testConfigPath));

        if (File.Exists(userConfigPath))
        {
            DateTime postTestWriteTime = File.GetLastWriteTimeUtc(userConfigPath);
            Assert.Equal(preTestWriteTime, postTestWriteTime);

            // Read user config content and assert no test leakage
            string userJson = File.ReadAllText(userConfigPath);
            Assert.DoesNotContain("AIBridge_McpServerTest_", userJson);
        }
    }

    [Fact]
    public void ProductionConfig_SanitizeTestPathsOnLoad()
    {
        var logService = new LogService();
        string testConfigPath = Path.Combine(_testDir, "leaked_config.json");
        var testConfigService = new ConfigService(logService, testConfigPath);

        var leakedCfg = new AppConfig
        {
            WorkspacePath = Path.Combine(_testDir, "AIBridge_McpServerTest_LeakedWorkspacePath")
        };
        testConfigService.SaveConfig(leakedCfg);

        // Load config -> should automatically sanitize leaked path
        var loaded = testConfigService.LoadConfig();
        Assert.Equal(string.Empty, loaded.WorkspacePath);
    }

    [Fact]
    public void HumanGate_CanApproveExecution_State_Test()
    {
        var logService = new LogService();
        var configService = new ConfigService(logService, Path.Combine(_testDir, "test_config.json"));
        var taskRegistry = new TaskRegistry();
        var taskService = new TaskService(logService, taskRegistry);
        var envService = new AntigravityEnvironmentService(logService);
        var runner = new AntigravityRunner(logService, envService);
        var bridgeServer = new BridgeServer(configService, logService, taskService, taskRegistry, envService, runner);

        var vm = new MainViewModel(configService, logService, taskService, envService, runner, bridgeServer);

        Assert.False(vm.CanApproveExecution());

        var plan = E2ETestProjectSetup.CreateMiniCalculatorPlan();
        vm.CurrentProjectPlan = plan;

        // Select Phase -> cannot approve
        vm.SelectedNode = plan.Phases[0];
        Assert.False(vm.CanApproveExecution());

        // Select Task that requires human approval -> can approve
        var taskWithApproval = plan.Phases[1].Tasks.First(t => t.RequiresHumanApproval);
        vm.SelectedNode = taskWithApproval;
        Assert.True(vm.CanApproveExecution());

        // Select Task that does not require human approval -> cannot approve
        var taskWithoutApproval = plan.Phases[0].Tasks.First(t => !t.RequiresHumanApproval);
        vm.SelectedNode = taskWithoutApproval;
        Assert.False(vm.CanApproveExecution());
    }

    [Fact]
    public async Task McpRegression_15Tools_And_NoHumanGateTool_NoWorkspacePathArg()
    {
        var logService = new LogService();
        var configService = new ConfigService(logService, Path.Combine(_testDir, "test_config.json"));
        var cfg = configService.LoadConfig();
        cfg.McpHost = "127.0.0.1";
        cfg.McpPort = 8797;
        cfg.ApiToken = "test-token";
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
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.EndpointUrl)
            });

            await using var client = await McpClient.CreateAsync(transport);

            var tools = (await client.ListToolsAsync()).ToList();

            Assert.Equal(15, tools.Count);
            Assert.DoesNotContain(tools, t => t.Name.Contains("approve", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("human", StringComparison.OrdinalIgnoreCase));

            foreach (var t in tools)
            {
                if (t.ProtocolTool.InputSchema.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                {
                    string schemaJson = System.Text.Json.JsonSerializer.Serialize(t.ProtocolTool.InputSchema);
                    Assert.DoesNotContain("workspacePath", schemaJson);
                }
            }
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
