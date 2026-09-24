using System.Windows;
using AIBridge.Services;
using AIBridge.ViewModels;
using AIBridge.Views;

namespace AIBridge;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ILogService logService = new LogService();
        IConfigService configService = new ConfigService(logService);
        ITaskService taskService = new TaskService(logService);

        var config = configService.LoadConfig();
        ICodingAgentRunner antigravityRunner = new AntigravityRunner(
            logService, 
            config.AntigravityTimeoutMinutes, 
            config.AutoApprovePermissions
        );

        var viewModel = new MainViewModel(configService, logService, taskService, antigravityRunner);

        var mainWindow = new MainWindow
        {
            DataContext = viewModel
        };

        mainWindow.Show();
    }
}
