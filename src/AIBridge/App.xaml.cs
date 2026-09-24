using System.Windows;
using AIBridge.Services;
using AIBridge.ViewModels;
using AIBridge.Views;

namespace AIBridge;

public partial class App : Application
{
    private IBridgeServer? _bridgeServer;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ILogService logService = new LogService();
        IConfigService configService = new ConfigService(logService);
        ITaskRegistry taskRegistry = new TaskRegistry();
        ITaskService taskService = new TaskService(logService, taskRegistry);
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
            taskService,
            taskRegistry,
            environmentService,
            antigravityRunner
        );

        var viewModel = new MainViewModel(
            configService, 
            logService, 
            taskService, 
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
        if (_bridgeServer != null)
        {
            await _bridgeServer.StopAsync();
            _bridgeServer.Dispose();
            _bridgeServer = null;
        }
        base.OnExit(e);
    }
}
