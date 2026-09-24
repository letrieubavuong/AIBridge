using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using AIBridge.Infrastructure;
using AIBridge.Models;
using AIBridge.Services;
using Microsoft.Win32;

namespace AIBridge.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly ILogService _logService;
    private readonly ITaskService _taskService;
    private readonly IAntigravityEnvironmentService _environmentService;
    private readonly ICodingAgentRunner _antigravityRunner;
    private readonly IBridgeServer _bridgeServer;

    private AppConfig _config = new();
    private string _antigravityPath = string.Empty;
    private string _antigravityStatus = "Unknown";
    private string _workspacePath = string.Empty;
    private string _workspaceStatus = "Unknown";
    private string _promptText = string.Empty;
    
    private string _currentTaskId = "-";
    private string _currentTaskStatusText = "Idle";
    private string _currentTaskStarted = "-";
    private string _currentTaskCompleted = "-";
    private bool _isTaskRunning;

    // Environment Setup Section Properties
    private string _cliStateText = "Checking...";
    private string _cliVersionText = "-";
    private string _authStateText = "Unknown";
    private bool _isCliMissing;
    private bool _isAuthRequired;

    // Bridge Section Properties
    private bool _isTokenMasked = true;

    public string AppVersionText
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
            return $"v{versionStr} (Phase 03)";
        }
    }

    public string BridgeStatusText => _bridgeServer?.Status.ToString().ToUpper() ?? "STOPPED";
    public string BridgeHostPortText => $"{_config.BridgeHost}:{_config.BridgePort}";
    public int BridgePort => _config.BridgePort;

    public string ApiTokenText => _config.ApiToken;
    public string DisplayedApiTokenText => IsTokenMasked && !string.IsNullOrEmpty(_config.ApiToken) 
        ? "••••••••••••••••" 
        : (string.IsNullOrEmpty(_config.ApiToken) ? "(None)" : _config.ApiToken);

    public bool IsTokenMasked
    {
        get => _isTokenMasked;
        set
        {
            if (SetProperty(ref _isTokenMasked, value))
            {
                OnPropertyChanged(nameof(DisplayedApiTokenText));
            }
        }
    }

    public bool IsBridgeRunning => _bridgeServer?.Status == BridgeStatus.Running;
    public bool IsBridgeStopped => _bridgeServer?.Status == BridgeStatus.Stopped || _bridgeServer?.Status == BridgeStatus.Error;

    public string AntigravityPath
    {
        get => _antigravityPath;
        set
        {
            if (SetProperty(ref _antigravityPath, value))
            {
                _config.AntigravityPath = value;
                AntigravityStatus = "Unknown";
                SaveConfiguration();
                _ = RefreshEnvironmentAsync();
            }
        }
    }

    public string AntigravityStatus
    {
        get => _antigravityStatus;
        set => SetProperty(ref _antigravityStatus, value);
    }

    public string WorkspacePath
    {
        get => _workspacePath;
        set
        {
            if (SetProperty(ref _workspacePath, value))
            {
                _config.WorkspacePath = value;
                WorkspaceStatus = "Unknown";
                SaveConfiguration();
            }
        }
    }

    public string WorkspaceStatus
    {
        get => _workspaceStatus;
        set => SetProperty(ref _workspaceStatus, value);
    }

    public string PromptText
    {
        get => _promptText;
        set => SetProperty(ref _promptText, value);
    }

    public string CurrentTaskId
    {
        get => _currentTaskId;
        set => SetProperty(ref _currentTaskId, value);
    }

    public string CurrentTaskStatusText
    {
        get => _currentTaskStatusText;
        set => SetProperty(ref _currentTaskStatusText, value);
    }

    public string CurrentTaskStarted
    {
        get => _currentTaskStarted;
        set => SetProperty(ref _currentTaskStarted, value);
    }

    public string CurrentTaskCompleted
    {
        get => _currentTaskCompleted;
        set => SetProperty(ref _currentTaskCompleted, value);
    }

    public bool IsTaskRunning
    {
        get => _isTaskRunning;
        set => SetProperty(ref _isTaskRunning, value);
    }

    // Environment Setup Card Properties
    public string CliStateText
    {
        get => _cliStateText;
        set => SetProperty(ref _cliStateText, value);
    }

    public string CliVersionText
    {
        get => _cliVersionText;
        set => SetProperty(ref _cliVersionText, value);
    }

    public string AuthStateText
    {
        get => _authStateText;
        set => SetProperty(ref _authStateText, value);
    }

    public bool IsCliMissing
    {
        get => _isCliMissing;
        set => SetProperty(ref _isCliMissing, value);
    }

    public bool IsAuthRequired
    {
        get => _isAuthRequired;
        set => SetProperty(ref _isAuthRequired, value);
    }

    public ObservableCollection<LogEntry> LogEntries => _logService.LogEntries;

    // Commands
    public ICommand BrowseAntigravityCommand { get; }
    public ICommand TestAntigravityCommand { get; }
    public ICommand BrowseWorkspaceCommand { get; }
    public ICommand ValidateWorkspaceCommand { get; }
    public ICommand SubmitTaskCommand { get; }
    public ICommand CancelTaskCommand { get; }
    public ICommand InstallCliCommand { get; }
    public ICommand CheckAuthCommand { get; }
    public ICommand AuthenticateCommand { get; }
    public ICommand StartBridgeCommand { get; }
    public ICommand StopBridgeCommand { get; }
    public ICommand CopyTokenCommand { get; }
    public ICommand RegenerateTokenCommand { get; }
    public ICommand ToggleTokenMaskCommand { get; }

    public MainViewModel(
        IConfigService configService,
        ILogService logService,
        ITaskService taskService,
        IAntigravityEnvironmentService environmentService,
        ICodingAgentRunner antigravityRunner,
        IBridgeServer bridgeServer)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _environmentService = environmentService ?? throw new ArgumentNullException(nameof(environmentService));
        _antigravityRunner = antigravityRunner ?? throw new ArgumentNullException(nameof(antigravityRunner));
        _bridgeServer = bridgeServer ?? throw new ArgumentNullException(nameof(bridgeServer));

        BrowseAntigravityCommand = new RelayCommand(ExecuteBrowseAntigravity);
        TestAntigravityCommand = new AsyncRelayCommand(ExecuteTestAntigravityAsync);
        BrowseWorkspaceCommand = new RelayCommand(ExecuteBrowseWorkspace);
        ValidateWorkspaceCommand = new AsyncRelayCommand(ExecuteValidateWorkspaceAsync);
        SubmitTaskCommand = new AsyncRelayCommand(ExecuteSubmitTaskAsync, CanSubmitTask);
        CancelTaskCommand = new RelayCommand(ExecuteCancelTask, CanCancelTask);
        InstallCliCommand = new AsyncRelayCommand(ExecuteInstallCliAsync);
        CheckAuthCommand = new AsyncRelayCommand(ExecuteCheckAuthAsync);
        AuthenticateCommand = new AsyncRelayCommand(ExecuteAuthenticateAsync);

        StartBridgeCommand = new AsyncRelayCommand(ExecuteStartBridgeAsync);
        StopBridgeCommand = new AsyncRelayCommand(ExecuteStopBridgeAsync);
        CopyTokenCommand = new RelayCommand(ExecuteCopyToken);
        RegenerateTokenCommand = new AsyncRelayCommand(ExecuteRegenerateTokenAsync);
        ToggleTokenMaskCommand = new RelayCommand(ExecuteToggleTokenMask);

        _taskService.CurrentTaskChanged += OnCurrentTaskChanged;
        _taskService.TaskUpdated += OnTaskUpdated;
        _environmentService.EnvironmentInfoChanged += OnEnvironmentInfoChanged;
        _bridgeServer.StatusChanged += OnBridgeStatusChanged;

        InitializeViewModel();
    }

    private void InitializeViewModel()
    {
        _logService.LogInfo("Initializing AIBridge Desktop Application...");
        _config = _configService.LoadConfig();

        _antigravityPath = _config.AntigravityPath;
        _workspacePath = _config.WorkspacePath;
        OnPropertyChanged(nameof(AntigravityPath));
        OnPropertyChanged(nameof(WorkspacePath));
        OnPropertyChanged(nameof(ApiTokenText));
        OnPropertyChanged(nameof(DisplayedApiTokenText));
        OnPropertyChanged(nameof(BridgeHostPortText));

        _logService.LogInfo("AIBridge Initialization Complete.");

        _ = RefreshEnvironmentAsync();

        if (!string.IsNullOrWhiteSpace(_workspacePath))
        {
            _ = ExecuteValidateWorkspaceAsync();
        }

        // Auto-start Local Bridge if enabled
        if (_config.BridgeEnabled)
        {
            _logService.LogInfo("Auto-starting Local Bridge Server...");
            _ = ExecuteStartBridgeAsync();
        }
    }

    private void OnBridgeStatusChanged(BridgeStatus newStatus)
    {
        RunOnUi(() =>
        {
            OnPropertyChanged(nameof(BridgeStatusText));
            OnPropertyChanged(nameof(IsBridgeRunning));
            OnPropertyChanged(nameof(IsBridgeStopped));
        });
    }

    private async Task ExecuteStartBridgeAsync()
    {
        await _bridgeServer.StartAsync();
        _config = _configService.LoadConfig();
        OnPropertyChanged(nameof(ApiTokenText));
        OnPropertyChanged(nameof(DisplayedApiTokenText));
    }

    private async Task ExecuteStopBridgeAsync()
    {
        await _bridgeServer.StopAsync();
    }

    private void ExecuteCopyToken()
    {
        if (!string.IsNullOrEmpty(_config.ApiToken))
        {
            try
            {
                Clipboard.SetText(_config.ApiToken);
                _logService.LogInfo("API Token copied to clipboard.");
            }
            catch (Exception ex)
            {
                _logService.LogError("Failed to copy API token to clipboard", ex);
            }
        }
    }

    private async Task ExecuteRegenerateTokenAsync()
    {
        var result = MessageBox.Show(
            "Are you sure you want to regenerate the API authentication token?\nExisting clients will be invalidated.",
            "Regenerate API Token",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var newToken = BridgeServer.GenerateSecureToken();
        _config.ApiToken = newToken;
        _configService.SaveConfig(_config);

        OnPropertyChanged(nameof(ApiTokenText));
        OnPropertyChanged(nameof(DisplayedApiTokenText));
        _logService.LogInfo("API Authentication Token regenerated successfully.");

        if (_bridgeServer.Status == BridgeStatus.Running)
        {
            _logService.LogInfo("Applying new token to active Bridge Server...");
        }
    }

    private void ExecuteToggleTokenMask()
    {
        IsTokenMasked = !IsTokenMasked;
    }

    private async Task RefreshEnvironmentAsync()
    {
        CliStateText = "Checking...";
        AuthStateText = "Checking...";
        AntigravityStatus = "Checking...";

        var info = await _environmentService.DetectAndVerifyEnvironmentAsync(AntigravityPath);
        UpdateEnvironmentUI(info);
    }

    private void UpdateEnvironmentUI(AntigravityEnvironmentInfo info)
    {
        RunOnUi(() =>
        {
            CliStateText = info.InstallationState switch
            {
                CliInstallationState.Ready => "READY",
                CliInstallationState.NotInstalled => "NOT INSTALLED",
                CliInstallationState.Error => "ERROR",
                _ => "CHECKING..."
            };

            CliVersionText = !string.IsNullOrWhiteSpace(info.Version) ? info.Version : "-";
            IsCliMissing = info.InstallationState == CliInstallationState.NotInstalled;

            AuthStateText = info.AuthState switch
            {
                CliAuthState.Ready => "READY",
                CliAuthState.Required => "REQUIRED",
                CliAuthState.Error => "ERROR",
                _ => "UNKNOWN"
            };

            IsAuthRequired = info.AuthState == CliAuthState.Required;

            AntigravityStatus = info.InstallationState == CliInstallationState.Ready ? "Ready" : "Not Found";
            if (!string.IsNullOrWhiteSpace(info.ExecutablePath) && string.IsNullOrWhiteSpace(AntigravityPath))
            {
                _antigravityPath = info.ExecutablePath;
                OnPropertyChanged(nameof(AntigravityPath));
            }
        });
    }

    private void OnEnvironmentInfoChanged(AntigravityEnvironmentInfo info)
    {
        UpdateEnvironmentUI(info);
    }

    private void SaveConfiguration()
    {
        _configService.SaveConfig(_config);
    }

    private void ExecuteBrowseAntigravity()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var agyBinDir = Path.Combine(localAppData, "agy", "bin");
            var initialDir = Directory.Exists(agyBinDir) ? agyBinDir : localAppData;

            var dialog = new OpenFileDialog
            {
                Title = "Select Antigravity CLI Executable (agy.exe)",
                Filter = "Executable files (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*",
                InitialDirectory = initialDir,
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                AntigravityPath = dialog.FileName;
                _logService.LogInfo($"Selected Antigravity CLI path: {AntigravityPath}");
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Error browsing for Antigravity CLI path", ex);
        }
    }

    private async Task ExecuteTestAntigravityAsync()
    {
        _logService.LogInfo("Starting Antigravity CLI validation and health check...");
        await RefreshEnvironmentAsync();
    }

    private async Task ExecuteInstallCliAsync()
    {
        var result = MessageBox.Show(
            "Do you want to install official Antigravity CLI ('agy') now?",
            "Install Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result != MessageBoxResult.Yes)
        {
            _logService.LogInfo("Antigravity CLI installation cancelled by user.");
            return;
        }

        _logService.LogInfo("User confirmed installation. Starting installer...");
        CliStateText = "Installing...";
        bool success = await _environmentService.InstallCliAsync();
        if (success)
        {
            _logService.LogInfo("Antigravity CLI installed and verified successfully.");
        }
        else
        {
            _logService.LogWarning("Antigravity CLI installation did not complete or failed verification.");
        }
        await RefreshEnvironmentAsync();
    }

    private async Task ExecuteCheckAuthAsync()
    {
        AuthStateText = "Checking...";
        var authState = await _environmentService.CheckAuthenticationAsync(AntigravityPath);
        AuthStateText = authState == CliAuthState.Ready ? "READY" : "REQUIRED";
    }

    private async Task ExecuteAuthenticateAsync()
    {
        await _environmentService.LaunchAuthenticationSetupAsync(AntigravityPath);
    }

    private void ExecuteBrowseWorkspace()
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Workspace Directory",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                WorkspacePath = dialog.FolderName;
                _logService.LogInfo($"Selected Workspace folder: {WorkspacePath}");
                _ = ExecuteValidateWorkspaceAsync();
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Error browsing for Workspace folder", ex);
        }
    }

    private async Task ExecuteValidateWorkspaceAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspacePath))
        {
            WorkspaceStatus = "Unknown";
            _logService.LogInfo("Workspace path is not configured.");
            return;
        }

        _logService.LogInfo("Validating Workspace path...");
        var (isValid, message) = await _antigravityRunner.ValidateWorkspaceAsync(WorkspacePath);
        WorkspaceStatus = isValid ? "Ready" : "Not Found";
        _logService.LogInfo($"Workspace status updated to: {WorkspaceStatus} ({message})");
    }

    private bool CanSubmitTask()
    {
        return !IsTaskRunning && !string.IsNullOrWhiteSpace(WorkspacePath);
    }

    private async Task ExecuteSubmitTaskAsync()
    {
        if (!_taskService.TryAcquireExecutionSlot())
        {
            _logService.LogWarning("Cannot submit task: A task is already running.");
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(WorkspacePath) || !Directory.Exists(WorkspacePath))
            {
                _logService.LogWarning("Cannot submit task: Workspace path is invalid or does not exist.");
                WorkspaceStatus = "Not Found";
                _taskService.ReleaseExecutionSlot();
                return;
            }

            if (string.IsNullOrWhiteSpace(PromptText))
            {
                _logService.LogWarning("Cannot submit task: Prompt is empty.");
                _taskService.ReleaseExecutionSlot();
                return;
            }

            var task = _taskService.CreateTask(PromptText, WorkspacePath, AntigravityPath);

            _logService.LogInfo($"Submitting task {task.Id} for real execution via Antigravity CLI...");

            var result = await _taskService.SubmitTaskAsync(task, _antigravityRunner);

            if (!result.Success)
            {
                _logService.LogWarning($"Task execution finish notice: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            _taskService.ReleaseExecutionSlot();
            _logService.LogError("Error during task submission", ex);
        }
    }

    private bool CanCancelTask()
    {
        return IsTaskRunning;
    }

    private void ExecuteCancelTask()
    {
        _taskService.CancelCurrentTask();
    }

    private void UpdateTaskUI(AgentTask? task)
    {
        RunOnUi(() =>
        {
            if (task == null)
            {
                CurrentTaskId = "-";
                CurrentTaskStatusText = "Idle";
                CurrentTaskStarted = "-";
                CurrentTaskCompleted = "-";
                IsTaskRunning = false;
                return;
            }

            CurrentTaskId = task.Id;
            CurrentTaskStatusText = task.Status.ToString();
            CurrentTaskStarted = task.StartedAt?.ToString("HH:mm:ss") ?? "-";
            CurrentTaskCompleted = task.CompletedAt?.ToString("HH:mm:ss") ?? "-";
            IsTaskRunning = task.Status == AgentTaskStatus.Running;
        });
    }

    private void OnCurrentTaskChanged(AgentTask? task)
    {
        UpdateTaskUI(task);
    }

    private void OnTaskUpdated(AgentTask task)
    {
        UpdateTaskUI(task);
    }

    private void RunOnUi(Action action)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
