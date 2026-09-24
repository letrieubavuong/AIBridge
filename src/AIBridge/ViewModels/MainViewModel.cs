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
    private readonly IGitEnvironmentService _gitEnvironmentService;
    private readonly IGitCommandService _gitCommandService;
    private readonly IGitEvidenceService _gitEvidenceService;
    private readonly ITaskRegistry _taskRegistry;
    private readonly IAIBrainService _brainService;
    private readonly IAIBrainProviderRegistry _brainProviderRegistry;

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

    // Git / GitHub Section Properties
    private string _gitStatusText = "Checking...";
    private string _gitVersionText = "-";
    private string _gitRepoText = "-";
    private string _gitBranchText = "-";
    private string _gitHeadText = "-";
    private string _gitWorkingTreeText = "-";
    private string _gitRemoteText = "-";
    private string _gitPushStateText = "-";
    private string _lastTaskCommitText = "-";
    private string _lastTaskChangedFilesText = "0 files";
    private bool _hasPreExistingChanges;
    private bool _hasEvidenceAvailable;
    private string _evidenceSummaryText = "No task evidence available yet.";

    // Bridge Section Properties
    private bool _isTokenMasked = true;

    // AI Brain Properties
    private BrainProviderDescriptor? _selectedBrainProvider;
    private string _brainModelText = "Development";
    private string _brainStatusText = "READY";
    private string _lastBrainRequestText = "-";
    private string _lastBrainDecisionText = "-";
    private string _lastBrainDurationText = "-";

    // Progress Tracking Properties
    private double _projectPercent = 33.3;
    private double _phasePercent = 30.0;
    private string _projectProgressText = "Phase 05 / 12 AI Brain Foundation";
    private string _phaseProgressText = "3 / 10 tasks completed | Current: Brain Provider Registry | Status: RUNNING";
    private string _automationModeText = "Manual";

    public string AppVersionText
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
            return $"v{versionStr} (Phase 05)";
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

    // Git Section Public Properties
    public string GitStatusText
    {
        get => _gitStatusText;
        set => SetProperty(ref _gitStatusText, value);
    }

    public string GitVersionText
    {
        get => _gitVersionText;
        set => SetProperty(ref _gitVersionText, value);
    }

    public string GitRepoText
    {
        get => _gitRepoText;
        set => SetProperty(ref _gitRepoText, value);
    }

    public string GitBranchText
    {
        get => _gitBranchText;
        set => SetProperty(ref _gitBranchText, value);
    }

    public string GitHeadText
    {
        get => _gitHeadText;
        set => SetProperty(ref _gitHeadText, value);
    }

    public string GitWorkingTreeText
    {
        get => _gitWorkingTreeText;
        set => SetProperty(ref _gitWorkingTreeText, value);
    }

    public string GitRemoteText
    {
        get => _gitRemoteText;
        set => SetProperty(ref _gitRemoteText, value);
    }

    public string GitPushStateText
    {
        get => _gitPushStateText;
        set => SetProperty(ref _gitPushStateText, value);
    }

    public string LastTaskCommitText
    {
        get => _lastTaskCommitText;
        set => SetProperty(ref _lastTaskCommitText, value);
    }

    public string LastTaskChangedFilesText
    {
        get => _lastTaskChangedFilesText;
        set => SetProperty(ref _lastTaskChangedFilesText, value);
    }

    public bool HasPreExistingChanges
    {
        get => _hasPreExistingChanges;
        set => SetProperty(ref _hasPreExistingChanges, value);
    }

    public bool HasEvidenceAvailable
    {
        get => _hasEvidenceAvailable;
        set => SetProperty(ref _hasEvidenceAvailable, value);
    }

    public string EvidenceSummaryText
    {
        get => _evidenceSummaryText;
        set => SetProperty(ref _evidenceSummaryText, value);
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
    public ICommand RefreshGitCommand { get; }
    public ICommand ViewEvidenceCommand { get; }
    public ICommand PushGitCommand { get; }

    public ObservableCollection<BrainProviderDescriptor> BrainProviders { get; } = new();

    public BrainProviderDescriptor? SelectedBrainProvider
    {
        get => _selectedBrainProvider;
        set
        {
            if (SetProperty(ref _selectedBrainProvider, value) && value != null)
            {
                _config.BrainProvider = value.Id;
                SaveConfiguration();
                _logService.LogInfo($"Selected AI Brain Provider changed to: {value.DisplayName} ({value.Id})");
            }
        }
    }

    public string BrainModelText
    {
        get => _brainModelText;
        set => SetProperty(ref _brainModelText, value);
    }

    public string BrainStatusText
    {
        get => _brainStatusText;
        set => SetProperty(ref _brainStatusText, value);
    }

    public string LastBrainRequestText
    {
        get => _lastBrainRequestText;
        set => SetProperty(ref _lastBrainRequestText, value);
    }

    public string LastBrainDecisionText
    {
        get => _lastBrainDecisionText;
        set => SetProperty(ref _lastBrainDecisionText, value);
    }

    public string LastBrainDurationText
    {
        get => _lastBrainDurationText;
        set => SetProperty(ref _lastBrainDurationText, value);
    }

    public double ProjectPercent
    {
        get => _projectPercent;
        set => SetProperty(ref _projectPercent, value);
    }

    public double PhasePercent
    {
        get => _phasePercent;
        set => SetProperty(ref _phasePercent, value);
    }

    public string ProjectProgressText
    {
        get => _projectProgressText;
        set => SetProperty(ref _projectProgressText, value);
    }

    public string PhaseProgressText
    {
        get => _phaseProgressText;
        set => SetProperty(ref _phaseProgressText, value);
    }

    public string AutomationModeText
    {
        get => _automationModeText;
        set => SetProperty(ref _automationModeText, value);
    }

    public ICommand TestBrainCommand { get; }
    public ICommand ViewLastBrainResponseCommand { get; }

    public MainViewModel(
        IConfigService configService,
        ILogService logService,
        ITaskService taskService,
        IAntigravityEnvironmentService environmentService,
        ICodingAgentRunner antigravityRunner,
        IBridgeServer bridgeServer,
        IGitEnvironmentService? gitEnvironmentService = null,
        IGitCommandService? gitCommandService = null,
        IGitEvidenceService? gitEvidenceService = null,
        ITaskRegistry? taskRegistry = null,
        IAIBrainService? brainService = null,
        IAIBrainProviderRegistry? brainProviderRegistry = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _environmentService = environmentService ?? throw new ArgumentNullException(nameof(environmentService));
        _antigravityRunner = antigravityRunner ?? throw new ArgumentNullException(nameof(antigravityRunner));
        _bridgeServer = bridgeServer ?? throw new ArgumentNullException(nameof(bridgeServer));

        _gitCommandService = gitCommandService ?? new GitCommandService(_logService);
        _gitEnvironmentService = gitEnvironmentService ?? new GitEnvironmentService(_logService, _gitCommandService);
        _gitEvidenceService = gitEvidenceService ?? new GitEvidenceService(_logService, _gitEnvironmentService, _gitCommandService);
        _taskRegistry = taskRegistry ?? new TaskRegistry();

        if (brainProviderRegistry == null)
        {
            var reg = new AIBrainProviderRegistry();
            reg.RegisterProvider(new MockBrainProvider());
            _brainProviderRegistry = reg;
        }
        else
        {
            _brainProviderRegistry = brainProviderRegistry;
        }

        _brainService = brainService ?? new AIBrainService(_logService, _configService, _brainProviderRegistry);

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

        RefreshGitCommand = new AsyncRelayCommand(ExecuteRefreshGitAsync);
        ViewEvidenceCommand = new RelayCommand(ExecuteViewEvidence);
        PushGitCommand = new AsyncRelayCommand(ExecutePushGitAsync);

        TestBrainCommand = new AsyncRelayCommand(ExecuteTestBrainAsync);
        ViewLastBrainResponseCommand = new RelayCommand(ExecuteViewLastBrainResponse);

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

        // Initialize Brain & Progress UI
        BrainProviders.Clear();
        var descriptors = _brainProviderRegistry.GetAvailableProviders();
        foreach (var desc in descriptors)
        {
            BrainProviders.Add(desc);
        }

        var activeId = _config.BrainProvider;
        SelectedBrainProvider = BrainProviders.FirstOrDefault(p => p.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase))
                               ?? BrainProviders.FirstOrDefault();

        BrainModelText = _config.BrainModel;
        BrainStatusText = _brainService.GetCurrentState().ToString().ToUpper();
        AutomationModeText = _config.AutomationMode.ToString();

        // Project progress: 4/12 phases completed (33.3%), Phase 05 = 3/10 tasks (30.0%)
        ProjectPercent = 33.3;
        PhasePercent = 30.0;
        ProjectProgressText = "Phase 05 / 12 AI Brain Foundation";
        PhaseProgressText = "3 / 10 tasks completed | Current: Brain Provider Registry | Status: RUNNING";
    }

    private async Task ExecuteTestBrainAsync()
    {
        _logService.LogInfo("Testing AI Brain Provider connectivity...");
        BrainStatusText = "THINKING";
        var success = await _brainService.TestActiveProviderAsync();
        var state = _brainService.GetCurrentState();
        BrainStatusText = state.ToString().ToUpper();

        if (success)
        {
            MessageBox.Show($"AI Brain Test Succeeded!\nProvider: {SelectedBrainProvider?.DisplayName}\nState: {state}", "AI Brain Test", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"AI Brain Test Failed!\nState: {state}", "AI Brain Test", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        UpdateBrainUI();
    }

    private void ExecuteViewLastBrainResponse()
    {
        var history = _brainService.GetDecisionHistory();
        var lastRecord = history.LastOrDefault();
        if (lastRecord == null)
        {
            MessageBox.Show("No AI Brain decision recorded yet.", "Last AI Brain Response", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Request ID: {lastRecord.RequestId}");
        sb.AppendLine($"Request Type: {lastRecord.RequestType}");
        sb.AppendLine($"Provider: {lastRecord.ProviderId}");
        sb.AppendLine($"Model: {lastRecord.Model}");
        sb.AppendLine($"Decision: {lastRecord.Decision}");
        sb.AppendLine($"Summary: {lastRecord.Summary}");
        sb.AppendLine($"Success: {lastRecord.Success}");
        if (!string.IsNullOrEmpty(lastRecord.ErrorCode))
        {
            sb.AppendLine($"Error Code: {lastRecord.ErrorCode}");
        }
        sb.AppendLine($"Started: {lastRecord.StartedAt:HH:mm:ss}");
        sb.AppendLine($"Completed: {lastRecord.CompletedAt:HH:mm:ss}");
        sb.AppendLine($"Duration: {lastRecord.DurationSeconds:F3}s");

        MessageBox.Show(sb.ToString(), "Last AI Brain Response", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UpdateBrainUI()
    {
        RunOnUi(() =>
        {
            BrainStatusText = _brainService.GetCurrentState().ToString().ToUpper();
            var history = _brainService.GetDecisionHistory();
            var lastRecord = history.LastOrDefault();
            if (lastRecord != null)
            {
                LastBrainRequestText = lastRecord.RequestType.ToString();
                LastBrainDecisionText = lastRecord.Decision.ToString();
                LastBrainDurationText = $"{lastRecord.DurationSeconds:F3}s";
            }
        });
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

    private async Task ExecuteRefreshGitAsync()
    {
        GitStatusText = "Checking...";
        var gitEnv = await _gitEnvironmentService.DetectAndVerifyEnvironmentAsync(WorkspacePath, _config.GitPath);

        RunOnUi(() =>
        {
            if (!gitEnv.GitInstalled)
            {
                GitStatusText = "NOT INSTALLED";
                GitVersionText = "-";
                GitRepoText = "-";
                GitBranchText = "-";
                GitHeadText = "-";
                GitWorkingTreeText = "-";
                GitRemoteText = "-";
                GitPushStateText = "-";
                return;
            }

            GitVersionText = !string.IsNullOrEmpty(gitEnv.Version) ? gitEnv.Version : "-";

            if (!gitEnv.IsRepository)
            {
                GitStatusText = "NOT A REPO";
                GitRepoText = "-";
                GitBranchText = "-";
                GitHeadText = "-";
                GitWorkingTreeText = "-";
                GitRemoteText = "-";
                GitPushStateText = "-";
                return;
            }

            GitStatusText = "READY";
            try
            {
                GitRepoText = !string.IsNullOrEmpty(gitEnv.RepositoryRoot) ? Path.GetFileName(gitEnv.RepositoryRoot) : "-";
            }
            catch
            {
                GitRepoText = gitEnv.RepositoryRoot;
            }

            GitBranchText = !string.IsNullOrEmpty(gitEnv.Branch) ? gitEnv.Branch : "HEAD";
            GitRemoteText = !string.IsNullOrEmpty(gitEnv.RemoteName) ? gitEnv.RemoteName : "None";
        });

        if (gitEnv.GitInstalled && gitEnv.IsRepository)
        {
            var gitExe = gitEnv.GitPath;
            var repoRoot = gitEnv.RepositoryRoot;
            var headSha = await _gitCommandService.GetHeadShaAsync(repoRoot, gitExe) ?? string.Empty;
            var headShort = headSha.Length >= 7 ? headSha[..7] : headSha;
            var (_, _, isDirty) = await _gitCommandService.GetStatusAsync(repoRoot, gitExe);
            var aheadBehind = await _gitCommandService.GetAheadBehindAsync(repoRoot, gitEnv.Branch, gitEnv.RemoteName, gitExe);

            RunOnUi(() =>
            {
                GitHeadText = headShort;
                GitWorkingTreeText = isDirty ? "DIRTY" : "CLEAN";
                if (string.IsNullOrEmpty(gitEnv.RemoteName))
                {
                    GitPushStateText = "NOT CONFIG";
                }
                else if (!aheadBehind.IsVerified)
                {
                    GitPushStateText = "UNKNOWN";
                }
                else if (aheadBehind.Ahead > 0)
                {
                    GitPushStateText = "NOT PUSHED";
                }
                else
                {
                    GitPushStateText = "PUSHED";
                }
            });
        }
    }

    private void ExecuteViewEvidence()
    {
        MessageBox.Show(
            EvidenceSummaryText,
            "Git Evidence Summary",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private async Task ExecutePushGitAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspacePath))
        {
            MessageBox.Show("Workspace path is not configured.", "Push Git", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var gitEnv = await _gitEnvironmentService.DetectAndVerifyEnvironmentAsync(WorkspacePath, _config.GitPath);
        if (!gitEnv.GitInstalled || !gitEnv.IsRepository)
        {
            MessageBox.Show("Workspace is not a valid Git repository.", "Push Git", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var prompt = $"Push current commits to remote '{gitEnv.RemoteName}' on branch '{gitEnv.Branch}'?";
        var confirm = MessageBox.Show(prompt, "Confirm Git Push", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _logService.LogInfo($"User triggered manual Git push to {gitEnv.RemoteName}/{gitEnv.Branch}...");
        var (success, pushState, message) = await _gitEvidenceService.PushTaskCommitAsync(CurrentTaskId, WorkspacePath, _config);

        if (success)
        {
            MessageBox.Show($"Git Push Succeeded: {message}", "Push Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"Git Push Failed ({pushState}): {message}", "Push Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        await ExecuteRefreshGitAsync();
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

        await ExecuteRefreshGitAsync();
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
                HasEvidenceAvailable = false;
                HasPreExistingChanges = false;
                LastTaskCommitText = "-";
                LastTaskChangedFilesText = "0 files";
                return;
            }

            CurrentTaskId = task.Id;
            CurrentTaskStatusText = task.Status.ToString();
            CurrentTaskStarted = task.StartedAt?.ToString("HH:mm:ss") ?? "-";
            CurrentTaskCompleted = task.CompletedAt?.ToString("HH:mm:ss") ?? "-";
            IsTaskRunning = task.Status == AgentTaskStatus.Running;

            // Fetch Evidence if available
            var evidence = _taskRegistry.GetGitEvidence(task.Id);
            if (evidence != null)
            {
                HasEvidenceAvailable = true;
                HasPreExistingChanges = evidence.PreExistingChanges;
                LastTaskCommitText = !string.IsNullOrEmpty(evidence.NewCommitShortSha) ? evidence.NewCommitShortSha : (evidence.AfterSnapshot?.HeadShortSha ?? "-");
                LastTaskChangedFilesText = $"{evidence.ChangedFiles.Count} files";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Task ID: {evidence.TaskId}");
                sb.AppendLine($"Repository: {evidence.IsRepository} ({evidence.AfterSnapshot?.RepositoryRoot})");
                sb.AppendLine($"Branch: {evidence.AfterSnapshot?.Branch} | Remote: {evidence.AfterSnapshot?.RemoteName}");
                sb.AppendLine($"Pre-existing Changes: {evidence.PreExistingChanges}");
                sb.AppendLine($"Head Changed: {evidence.HeadChanged}");
                sb.AppendLine($"New Commit Detected: {evidence.NewCommitDetected}");

                if (evidence.NewCommitDetected)
                {
                    sb.AppendLine($"Commit SHA: {evidence.NewCommitSha}");
                    sb.AppendLine($"Commit Message: {evidence.NewCommitMessage}");
                }

                sb.AppendLine($"Changed Files Count: {evidence.ChangedFiles.Count}");
                foreach (var cf in evidence.ChangedFiles.Take(10))
                {
                    sb.AppendLine($"  - [{cf.Status}] {cf.Path}");
                }

                if (evidence.ChangedFiles.Count > 10)
                {
                    sb.AppendLine($"  ... and {evidence.ChangedFiles.Count - 10} more files.");
                }

                sb.AppendLine($"Push State: {evidence.PushState}");
                if (!string.IsNullOrEmpty(evidence.Diff))
                {
                    sb.AppendLine("\n--- Diff Preview ---");
                    sb.AppendLine(evidence.Diff.Length > 500 ? evidence.Diff.Substring(0, 500) + "\n..." : evidence.Diff);
                }

                EvidenceSummaryText = sb.ToString();
            }
            else
            {
                HasEvidenceAvailable = false;
                HasPreExistingChanges = false;
            }
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
