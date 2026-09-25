using System.Windows;
using AIBridge.Models;
using AIBridge.Services;
using AIBridge.ViewModels;
using AIBridge.Views;

namespace AIBridge;

public partial class App : Application
{
    private IBridgeServer? _bridgeServer;
    private IMcpServer? _mcpServer;
    private ITaskService? _taskService;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        ILogService logService = new LogService();
        IConfigService configService = new ConfigService(logService);
        ITaskRegistry taskRegistry = new TaskRegistry();
        IGitCommandService gitCmdService = new GitCommandService(logService);
        IGitEnvironmentService gitEnvService = new GitEnvironmentService(logService, gitCmdService);
        IGitEvidenceService gitEvidenceService = new GitEvidenceService(logService, gitEnvService, gitCmdService);

        _taskService = new TaskService(logService, taskRegistry, gitEvidenceService, configService);
        IAntigravityEnvironmentService environmentService = new AntigravityEnvironmentService(logService);

        var config = configService.LoadConfig();
        ICodingAgentRunner antigravityRunner = new AntigravityRunner(
            logService, 
            environmentService,
            config.AntigravityTimeoutMinutes
        );

        IAIBrainProviderRegistry brainProviderRegistry = new AIBrainProviderRegistry();
        brainProviderRegistry.RegisterProvider(new MockBrainProvider());

        IAIBrainService brainService = new AIBrainService(logService, configService, brainProviderRegistry);
        var planStore = new FileProjectPlanStore();
        var planValidator = new PlanValidator();
        IPlanningService planningService = new PlanningService(brainService, planValidator, planStore);
        IPromptValidator promptValidator = new PromptValidator();
        IExecutionPromptService executionPromptService = new ExecutionPromptService(promptValidator, gitEvidenceService, configService, logService);
        IHumanApprovalService humanApprovalService = new HumanApprovalService();
        ICodingAgentRegistry codingAgentRegistry = new CodingAgentRegistry();
        codingAgentRegistry.RegisterAgent(new AntigravityCodingAgent(_taskService, antigravityRunner, environmentService));
        ICodingAgentService codingAgentService = new CodingAgentService(codingAgentRegistry, _taskService, planStore, promptValidator, gitEvidenceService, taskRegistry, humanApprovalService, logService);

        _mcpServer = new McpServer(
            configService,
            logService,
            _taskService,
            planningService,
            executionPromptService,
            codingAgentService,
            humanApprovalService,
            taskRegistry
        );

        if (config.McpEnabled)
        {
            await _mcpServer.StartAsync();
        }

        _bridgeServer = new BridgeServer(
            configService,
            logService,
            _taskService,
            taskRegistry,
            environmentService,
            antigravityRunner,
            gitEnvService,
            gitCmdService,
            gitEvidenceService,
            brainService,
            brainProviderRegistry
        );

        var viewModel = new MainViewModel(
            configService, 
            logService, 
            _taskService, 
            environmentService, 
            antigravityRunner,
            _bridgeServer,
            gitEnvService,
            gitCmdService,
            gitEvidenceService,
            taskRegistry,
            brainService,
            brainProviderRegistry,
            planningService,
            executionPromptService,
            codingAgentRegistry,
            codingAgentService,
            humanApprovalService,
            _mcpServer
        );

        var mainWindow = new MainWindow
        {
            DataContext = viewModel
        };

        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            // 1. Stop accepting new API requests
            if (_bridgeServer != null)
            {
                await _bridgeServer.StopAsync();
            }

            if (_mcpServer != null)
            {
                await _mcpServer.StopAsync();
            }

            // 2. If a task is running, request cancellation
            if (_taskService != null && _taskService.CurrentTask?.Status == AgentTaskStatus.Running)
            {
                _taskService.CancelCurrentTask();

                // 3. Wait for cancellation & process tree termination with a bounded timeout (max 10s)
                await _taskService.WaitForCurrentTaskToCompleteAsync(TimeSpan.FromSeconds(10));
            }

            // 4. Dispose server and resources
            _bridgeServer?.Dispose();
            _bridgeServer = null;

            if (_mcpServer is IDisposable disp)
            {
                disp.Dispose();
            }
            _mcpServer = null;
        }
        catch
        {
            // Suppress exception during shutdown
        }
        finally
        {
            base.OnExit(e);
        }
    }
}
