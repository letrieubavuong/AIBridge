using System.Windows;
using AIBridge.Models;
using AIBridge.Services;
using AIBridge.ViewModels;
using AIBridge.Views;

namespace AIBridge;

public partial class App : Application
{
    private IBridgeServer? _bridgeServer;
    private ITaskService? _taskService;

    private void Application_Startup(object sender, StartupEventArgs e)
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

        _bridgeServer = new BridgeServer(
            configService,
            logService,
            _taskService,
            taskRegistry,
            environmentService,
            antigravityRunner,
            gitEnvService,
            gitCmdService,
            gitEvidenceService
        );

        var viewModel = new MainViewModel(
            configService, 
            logService, 
            _taskService, 
            environmentService, 
            antigravityRunner,
            _bridgeServer
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
