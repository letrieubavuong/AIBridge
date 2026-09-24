using System.Collections.ObjectModel;
using System.IO;
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
    private readonly ICodingAgentRunner _antigravityRunner;

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

    public string AppVersionText => "v1.0.0 (Phase 01)";
    public string BridgeStatusText => "Ready";
    public int BridgePort => _config.BridgePort;

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

    public ObservableCollection<LogEntry> LogEntries => _logService.LogEntries;

    // Commands
    public ICommand BrowseAntigravityCommand { get; }
    public ICommand TestAntigravityCommand { get; }
    public ICommand BrowseWorkspaceCommand { get; }
    public ICommand ValidateWorkspaceCommand { get; }
    public ICommand SubmitTaskCommand { get; }
    public ICommand CancelTaskCommand { get; }

    public MainViewModel(
        IConfigService configService,
        ILogService logService,
        ITaskService taskService,
        ICodingAgentRunner antigravityRunner)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _antigravityRunner = antigravityRunner ?? throw new ArgumentNullException(nameof(antigravityRunner));

        BrowseAntigravityCommand = new RelayCommand(ExecuteBrowseAntigravity);
        TestAntigravityCommand = new AsyncRelayCommand(ExecuteTestAntigravityAsync);
        BrowseWorkspaceCommand = new RelayCommand(ExecuteBrowseWorkspace);
        ValidateWorkspaceCommand = new AsyncRelayCommand(ExecuteValidateWorkspaceAsync);
        SubmitTaskCommand = new AsyncRelayCommand(ExecuteSubmitTaskAsync, CanSubmitTask);
        CancelTaskCommand = new RelayCommand(ExecuteCancelTask, CanCancelTask);

        _taskService.CurrentTaskChanged += OnCurrentTaskChanged;

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

        _logService.LogInfo("AIBridge Initialization Complete.");

        if (!string.IsNullOrWhiteSpace(_antigravityPath))
        {
            _ = ExecuteTestAntigravityAsync();
        }

        if (!string.IsNullOrWhiteSpace(_workspacePath))
        {
            _ = ExecuteValidateWorkspaceAsync();
        }
    }

    private void SaveConfiguration()
    {
        _configService.SaveConfig(_config);
    }

    private void ExecuteBrowseAntigravity()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Antigravity Executable or Command",
                Filter = "Executable files (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*",
                CheckFileExists = false
            };

            if (dialog.ShowDialog() == true)
            {
                AntigravityPath = dialog.FileName;
                _logService.LogInfo($"Selected Antigravity path: {AntigravityPath}");
                _ = ExecuteTestAntigravityAsync();
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Error browsing for Antigravity path", ex);
        }
    }

    private async Task ExecuteTestAntigravityAsync()
    {
        _logService.LogInfo("Starting Antigravity configuration validation...");
        var (isValid, message) = await _antigravityRunner.ValidateConfigurationAsync(AntigravityPath);
        AntigravityStatus = isValid ? "Ready" : "Not Found";
        _logService.LogInfo($"Antigravity status updated to: {AntigravityStatus} ({message})");
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
        try
        {
            if (string.IsNullOrWhiteSpace(WorkspacePath) || !Directory.Exists(WorkspacePath))
            {
                _logService.LogWarning("Cannot submit task: Workspace path is invalid or does not exist.");
                WorkspaceStatus = "Not Found";
                return;
            }

            if (string.IsNullOrWhiteSpace(PromptText))
            {
                _logService.LogWarning("Cannot submit task: Prompt is empty.");
                return;
            }

            IsTaskRunning = true;
            var task = _taskService.CreateTask(PromptText, WorkspacePath);

            _logService.LogInfo($"Submitting task {task.Id} for execution...");

            var result = await _taskService.SubmitTaskAsync(task, _antigravityRunner);

            if (!result.Success)
            {
                _logService.LogWarning($"Task finish state notice: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Error during task submission", ex);
        }
        finally
        {
            IsTaskRunning = false;
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

    private void OnCurrentTaskChanged(AgentTask? task)
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
    }
}
