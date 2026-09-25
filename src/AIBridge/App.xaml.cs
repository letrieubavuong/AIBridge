using System;
using System.IO;
using System.Threading.Tasks;
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
    private ITunnelService? _tunnelService;
    private ITaskService? _taskService;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        SetupUnhandledExceptionHandling();

        WriteStartupLog("INIT", $"Starting AIBridge v{GetAppVersion()} on {Environment.OSVersion} (.NET {Environment.Version})");

        MainWindow? mainWindow = null;
        MainViewModel? viewModel = null;
        ILogService logService = new LogService();
        IConfigService configService = new ConfigService(logService);

        try
        {
            WriteStartupLog("STAGE_1", "Loading configuration...");
            var config = configService.LoadConfig();

            WriteStartupLog("STAGE_2", "Initializing core services...");
            ITaskRegistry taskRegistry = new TaskRegistry();
            IGitCommandService gitCmdService = new GitCommandService(logService);
            IGitEnvironmentService gitEnvService = new GitEnvironmentService(logService, gitCmdService);
            IGitEvidenceService gitEvidenceService = new GitEvidenceService(logService, gitEnvService, gitCmdService);

            _taskService = new TaskService(logService, taskRegistry, gitEvidenceService, configService);
            IAntigravityEnvironmentService environmentService = new AntigravityEnvironmentService(logService);

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

            _tunnelService = new CloudflareTunnelService(configService, logService);
            IMcpSelfTestService mcpSelfTestService = new McpSelfTestService(logService);

            WriteStartupLog("STAGE_3", "Creating MainViewModel and MainWindow...");
            viewModel = new MainViewModel(
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
                _mcpServer,
                _tunnelService,
                mcpSelfTestService
            );

            mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            WriteStartupLog("STAGE_4", "Displaying MainWindow UI...");
            mainWindow.Show();
            WriteStartupLog("STAGE_4", "MainWindow displayed successfully.");
        }
        catch (Exception ex)
        {
            WriteStartupLog("FATAL_STARTUP", "Fatal exception during UI creation", ex);
            MessageBox.Show(
                $"AIBridge không thể khởi động.\n\nChi tiết lỗi: {ex.Message}",
                "Lỗi khởi động AIBridge",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Shutdown(1);
            return;
        }

        // Asynchronous non-blocking background initialization AFTER MainWindow is shown
        _ = Task.Run(async () =>
        {
            var cfg = configService.LoadConfig();

            // Background MCP Auto-Start
            if (cfg.McpEnabled && _mcpServer != null)
            {
                WriteStartupLog("ASYNC_INIT", "Starting MCP Server in background...");
                try
                {
                    bool mcpOk = await _mcpServer.StartAsync();
                    if (!mcpOk)
                    {
                        WriteStartupLog("ASYNC_INIT", "MCP Server failed to start (port occupied or binding error).");
                    }
                    else
                    {
                        WriteStartupLog("ASYNC_INIT", "MCP Server started successfully in background.");
                    }
                }
                catch (Exception ex)
                {
                    WriteStartupLog("ASYNC_INIT", "MCP Server auto-start exception", ex);
                }
            }

            // Background Bridge Auto-Start
            if (cfg.BridgeEnabled && _bridgeServer != null)
            {
                WriteStartupLog("ASYNC_INIT", "Starting Local Bridge Server in background...");
                try
                {
                    await _bridgeServer.StartAsync();
                    WriteStartupLog("ASYNC_INIT", "Local Bridge Server started successfully in background.");
                }
                catch (Exception ex)
                {
                    WriteStartupLog("ASYNC_INIT", "Local Bridge Server auto-start exception", ex);
                }
            }
        });
    }

    private void SetupUnhandledExceptionHandling()
    {
        DispatcherUnhandledException += (s, args) =>
        {
            WriteStartupLog("UNHANDLED_DISPATCHER", "Unhandled WPF Dispatcher Exception", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                WriteStartupLog("UNHANDLED_APPDOMAIN", "Unhandled AppDomain Exception", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            WriteStartupLog("UNOBSERVED_TASK", "Unobserved Task Exception", args.Exception);
            args.SetObserved();
        };
    }

    private static string GetAppVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
    }

    private static void WriteStartupLog(string stage, string message, Exception? ex = null)
    {
        try
        {
            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIBridge", "logs");
            Directory.CreateDirectory(logsDir);
            var logFile = Path.Combine(logsDir, "startup.log");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stage}] {message}");
            if (ex != null)
            {
                sb.AppendLine($"  ExceptionType: {ex.GetType().FullName}");
                sb.AppendLine($"  Message: {ex.Message}");
                sb.AppendLine($"  StackTrace:\n{ex.StackTrace}");
            }
            File.AppendAllText(logFile, sb.ToString());
        }
        catch { }
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

            if (_tunnelService != null)
            {
                await _tunnelService.StopTunnelAsync();
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

            _tunnelService?.Dispose();
            _tunnelService = null;
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
