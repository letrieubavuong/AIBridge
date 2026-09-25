using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using AIBridge.Models;

namespace AIBridge.Services;

public class McpServer : IMcpServer, IDisposable
{
    private readonly IConfigService _configService;
    private readonly ILogService _logService;
    private readonly ITaskService _taskService;
    private readonly IPlanningService _planningService;
    private readonly IExecutionPromptService _executionPromptService;
    private readonly ICodingAgentService _codingAgentService;
    private readonly IHumanApprovalService _humanApprovalService;
    private readonly ITaskRegistry _taskRegistry;

    private WebApplication? _webApp;
    private bool _isRunning;
    private readonly object _lock = new();

    public event EventHandler<string>? LogMessage;
    public event EventHandler<bool>? StatusChanged;

    public bool IsRunning => _isRunning;
    public string BindAddress { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 8788;
    public string EndpointUrl => $"http://{BindAddress}:{Port}/mcp";

    public McpServer(
        IConfigService configService,
        ILogService logService,
        ITaskService taskService,
        IPlanningService planningService,
        IExecutionPromptService executionPromptService,
        ICodingAgentService codingAgentService,
        IHumanApprovalService humanApprovalService,
        ITaskRegistry? taskRegistry = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _planningService = planningService ?? throw new ArgumentNullException(nameof(planningService));
        _executionPromptService = executionPromptService ?? throw new ArgumentNullException(nameof(executionPromptService));
        _codingAgentService = codingAgentService ?? throw new ArgumentNullException(nameof(codingAgentService));
        _humanApprovalService = humanApprovalService ?? throw new ArgumentNullException(nameof(humanApprovalService));
        _taskRegistry = taskRegistry ?? new TaskRegistry();
    }

    public async Task<bool> StartAsync()
    {
        lock (_lock)
        {
            if (_isRunning) return true;
        }

        var cfg = _configService.LoadConfig();
        BindAddress = string.IsNullOrWhiteSpace(cfg.McpHost) ? "127.0.0.1" : cfg.McpHost;
        Port = cfg.McpPort > 0 ? cfg.McpPort : 8788;

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>()
            });

            builder.Logging.ClearProviders();

            builder.WebHost.ConfigureKestrel(options =>
            {
                if (IPAddress.TryParse(BindAddress, out var parsedIp))
                {
                    options.Listen(parsedIp, Port);
                }
                else
                {
                    options.ListenLocalhost(Port);
                }
            });

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton(_configService);
            builder.Services.AddSingleton(_logService);
            builder.Services.AddSingleton(_taskService);
            builder.Services.AddSingleton(_planningService);
            builder.Services.AddSingleton(_executionPromptService);
            builder.Services.AddSingleton(_codingAgentService);
            builder.Services.AddSingleton(_humanApprovalService);
            builder.Services.AddSingleton(_taskRegistry);
            builder.Services.AddSingleton(this);

            builder.Services.AddMcpServer()
                .WithHttpTransport()
                .WithTools<AIBridgeMcpToolHandlers>();

            var app = builder.Build();
            app.MapMcp("/mcp");

            _webApp = app;
            await app.StartAsync();

            lock (_lock)
            {
                _isRunning = true;
            }

            Log("MCP Server started at " + EndpointUrl);
            StatusChanged?.Invoke(this, true);
            return true;
        }
        catch (Exception ex)
        {
            Log("Failed to start MCP Server: " + ex.Message);
            _logService.LogError("MCP Server start error", ex);
            _webApp = null;
            lock (_lock)
            {
                _isRunning = false;
            }
            StatusChanged?.Invoke(this, false);
            return false;
        }
    }

    public async Task StopAsync()
    {
        WebApplication? appToStop = null;
        lock (_lock)
        {
            if (!_isRunning) return;
            Log("Stopping MCP Server...");
            appToStop = _webApp;
            _webApp = null;
            _isRunning = false;
        }

        if (appToStop != null)
        {
            try
            {
                await appToStop.StopAsync();
                await appToStop.DisposeAsync();
            }
            catch { }
        }

        StatusChanged?.Invoke(this, false);
        Log("MCP Server stopped.");
    }

    public void Log(string message)
    {
        _logService.LogInfo($"[MCP] {message}");
        LogMessage?.Invoke(this, message);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}

public class AIBridgeMcpToolHandlers
{
    private readonly IConfigService _configService;
    private readonly ILogService _logService;
    private readonly ITaskService _taskService;
    private readonly IPlanningService _planningService;
    private readonly IExecutionPromptService _executionPromptService;
    private readonly ICodingAgentService _codingAgentService;
    private readonly IHumanApprovalService _humanApprovalService;
    private readonly ITaskRegistry _taskRegistry;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly McpServer _mcpServer;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public AIBridgeMcpToolHandlers(
        IConfigService configService,
        ILogService logService,
        ITaskService taskService,
        IPlanningService planningService,
        IExecutionPromptService executionPromptService,
        ICodingAgentService codingAgentService,
        IHumanApprovalService humanApprovalService,
        ITaskRegistry taskRegistry,
        IHttpContextAccessor httpContextAccessor,
        McpServer mcpServer)
    {
        _configService = configService;
        _logService = logService;
        _taskService = taskService;
        _planningService = planningService;
        _executionPromptService = executionPromptService;
        _codingAgentService = codingAgentService;
        _humanApprovalService = humanApprovalService;
        _taskRegistry = taskRegistry;
        _httpContextAccessor = httpContextAccessor;
        _mcpServer = mcpServer;
    }

    private bool ValidateAuth()
    {
        var cfg = _configService.LoadConfig();
        string expectedToken = cfg.ApiToken ?? string.Empty;
        if (string.IsNullOrEmpty(expectedToken)) return true;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return true;

        string? token = null;
        string? authHeader = httpContext.Request.Headers["Authorization"];
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader.Substring(7).Trim();
        }
        if (string.IsNullOrEmpty(token))
        {
            token = httpContext.Request.Headers["X-AIBridge-Token"];
        }
        if (string.IsNullOrEmpty(token))
        {
            token = httpContext.Request.Query["token"];
        }

        if (string.IsNullOrEmpty(token)) return false;

        return ConstantTimeEquals(token, expectedToken);
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        byte[] aBytes = Encoding.UTF8.GetBytes(a);
        byte[] bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private bool EnsureAuthenticated(string toolName, out string? errorResult)
    {
        errorResult = null;
        if (toolName != "ping_bridge" && !ValidateAuth())
        {
            _mcpServer.Log($"MCP tool call '{toolName}' rejected: UNAUTHORIZED.");
            errorResult = FormatError("UNAUTHORIZED", "Invalid or missing API token.");
            return false;
        }
        return true;
    }

    private static string FormatResult(object data) => JsonSerializer.Serialize(data, JsonOpts);

    private static string FormatError(string errorCode, string message)
    {
        string normCode = errorCode switch
        {
            "HumanApprovalRequired" => "HUMAN_APPROVAL_REQUIRED",
            "DependencyNotSatisfied" => "DEPENDENCY_NOT_SATISFIED",
            "TaskNotDispatchable" => "TASK_NOT_DISPATCHABLE",
            "PromptStale" => "PROMPT_STALE",
            "PromptInvalid" => "PROMPT_INVALID",
            "ConcurrencyConflict" => "CONCURRENCY_CONFLICT",
            "WorkspaceInvalid" => "WORKSPACE_INVALID",
            "CodingAgentNotAvailable" => "CODING_AGENT_NOT_AVAILABLE",
            "TaskNotFound" => "TASK_NOT_FOUND",
            "ProjectNotFound" => "PROJECT_NOT_FOUND",
            "PhaseNotFound" => "PHASE_NOT_FOUND",
            _ => errorCode.ToUpperInvariant()
        };

        return JsonSerializer.Serialize(new { success = false, errorCode = normCode, errorMessage = message }, JsonOpts);
    }

    [McpServerTool(Name = "ping_bridge")]
    [Description("Check connectivity and readiness of the AIBridge MCP server. Does not reveal secrets.")]
    public string PingBridge()
    {
        return FormatResult(new
        {
            ok = true,
            service = "AIBridge",
            phase = "08",
            version = "0.8.0",
            mcpReady = true
        });
    }

    [McpServerTool(Name = "get_bridge_status")]
    [Description("Get operational status of AIBridge including workspace, Git, Antigravity CLI, and active execution status.")]
    public async Task<string> GetBridgeStatusAsync()
    {
        if (!EnsureAuthenticated("get_bridge_status", out var err)) return err!;
        var cfg = _configService.LoadConfig();
        var plans = await _planningService.GetProjectsAsync();
        var activePlan = plans.FirstOrDefault();
        var currentTask = _taskService.CurrentTask;
        var status = new
        {
            version = "0.8.0",
            restStatus = "RUNNING (port 8787)",
            mcpStatus = $"RUNNING (port {_mcpServer.Port})",
            workspaceConfigured = !string.IsNullOrWhiteSpace(cfg.WorkspacePath),
            gitAvailable = true,
            antigravityAvailable = true,
            selectedCodingAgent = cfg.CodingAgentProvider,
            activeExecution = currentTask != null ? new
            {
                taskId = currentTask.Id,
                status = currentTask.Status.ToString()
            } : null,
            automationMode = cfg.AutomationMode.ToString(),
            currentProject = activePlan?.ProjectId
        };
        return FormatResult(status);
    }

    [McpServerTool(Name = "list_projects")]
    [Description("List project plan summaries available in AIBridge.")]
    public async Task<string> ListProjectsAsync()
    {
        if (!EnsureAuthenticated("list_projects", out var err)) return err!;
        var cfg = _configService.LoadConfig();
        var plans = await _planningService.GetProjectsAsync();
        var list = plans.Select(p =>
        {
            var prog = _planningService.CalculateProjectProgress(p);
            return new
            {
                projectId = p.ProjectId,
                name = p.Name,
                planVersion = p.Version,
                status = p.Status.ToString(),
                automationMode = cfg.AutomationMode.ToString(),
                projectProgressPercent = prog.PercentComplete,
                currentPhase = prog.CurrentPhaseNumber
            };
        }).ToList();
        return FormatResult(list);
    }

    [McpServerTool(Name = "get_project")]
    [Description("Get full structured project plan details by ProjectId.")]
    public async Task<string> GetProjectAsync([Description("The Project ID")] string projectId)
    {
        if (!EnsureAuthenticated("get_project", out var err)) return err!;
        if (string.IsNullOrEmpty(projectId))
            return FormatError("PROJECT_NOT_FOUND", "projectId argument is required");

        var plan = await _planningService.GetPlanAsync(projectId);
        if (plan == null)
            return FormatError("PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

        var prog = _planningService.CalculateProjectProgress(plan);
        return FormatResult(new
        {
            project = plan,
            progress = prog
        });
    }

    [McpServerTool(Name = "get_project_progress")]
    [Description("Get quantitative progress metrics for a project.")]
    public async Task<string> GetProjectProgressAsync([Description("The Project ID")] string projectId)
    {
        if (!EnsureAuthenticated("get_project_progress", out var err)) return err!;
        if (string.IsNullOrEmpty(projectId))
            return FormatError("PROJECT_NOT_FOUND", "projectId argument is required");

        var plan = await _planningService.GetPlanAsync(projectId);
        if (plan == null)
            return FormatError("PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

        var prog = _planningService.CalculateProjectProgress(plan);
        var currentPhase = plan.Phases.FirstOrDefault(p => p.PhaseNumber == prog.CurrentPhaseNumber);
        var phaseProg = currentPhase != null ? _planningService.CalculatePhaseProgress(currentPhase) : null;

        int passedTasks = plan.Phases.Sum(p => p.Tasks.Count(t => t.Status == TaskPlanStatus.Passed));
        int totalTasks = plan.Phases.Sum(p => p.Tasks.Count);
        string currentTaskId = plan.Phases.SelectMany(p => p.Tasks).FirstOrDefault(t => t.Status == TaskPlanStatus.Running || t.Status == TaskPlanStatus.Reviewing || t.Status == TaskPlanStatus.NotStarted)?.TaskId ?? string.Empty;

        return FormatResult(new
        {
            projectPercent = prog.PercentComplete,
            currentPhasePercent = phaseProg?.PercentComplete ?? 0,
            completedPhases = prog.CompletedPhases,
            totalPhases = prog.TotalPhases,
            passedLogicalTasks = passedTasks,
            totalLogicalTasks = totalTasks,
            currentTaskId = currentTaskId,
            status = prog.Status
        });
    }

    [McpServerTool(Name = "get_current_phase")]
    [Description("Get the authoritative active phase for a project.")]
    public async Task<string> GetCurrentPhaseAsync([Description("The Project ID")] string projectId)
    {
        if (!EnsureAuthenticated("get_current_phase", out var err)) return err!;
        if (string.IsNullOrEmpty(projectId))
            return FormatError("PROJECT_NOT_FOUND", "projectId argument is required");

        var plan = await _planningService.GetPlanAsync(projectId);
        if (plan == null)
            return FormatError("PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

        var prog = _planningService.CalculateProjectProgress(plan);
        var currentPhase = plan.Phases.FirstOrDefault(p => p.PhaseNumber == prog.CurrentPhaseNumber)
                           ?? plan.Phases.FirstOrDefault(p => p.Status == PhaseStatus.Running)
                           ?? plan.Phases.FirstOrDefault();

        if (currentPhase == null)
            return FormatError("PHASE_NOT_FOUND", "No phase found in project");

        return FormatResult(currentPhase);
    }

    [McpServerTool(Name = "get_dispatchable_tasks")]
    [Description("Get tasks eligible for execution under Phase 07 dispatch policy. Tasks requiring pending Human Gate approval return dispatchable=false with reason HUMAN_APPROVAL_REQUIRED.")]
    public async Task<string> GetDispatchableTasksAsync([Description("The Project ID")] string projectId)
    {
        if (!EnsureAuthenticated("get_dispatchable_tasks", out var err)) return err!;
        if (string.IsNullOrEmpty(projectId))
            return FormatError("PROJECT_NOT_FOUND", "projectId argument is required");

        var plan = await _planningService.GetPlanAsync(projectId);
        if (plan == null)
            return FormatError("PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

        var dispatchable = _codingAgentService.GetDispatchableTasks(plan);
        var resultList = new List<object>();

        foreach (var task in dispatchable)
        {
            if (task.RequiresHumanApproval)
            {
                var validApproval = await _humanApprovalService.GetValidApprovalAsync(plan.ProjectId, task.PhaseId, task.TaskId, plan.Version);
                if (validApproval == null)
                {
                    resultList.Add(new
                    {
                        taskId = task.TaskId,
                        phaseId = task.PhaseId,
                        title = task.Title,
                        requiresHumanApproval = true,
                        dispatchable = false,
                        reason = "HUMAN_APPROVAL_REQUIRED"
                    });
                    continue;
                }
            }

            resultList.Add(new
            {
                taskId = task.TaskId,
                phaseId = task.PhaseId,
                title = task.Title,
                requiresHumanApproval = task.RequiresHumanApproval,
                dispatchable = true,
                status = task.Status.ToString()
            });
        }

        return FormatResult(resultList);
    }

    [McpServerTool(Name = "get_task")]
    [Description("Get detailed specification of a single task.")]
    public async Task<string> GetTaskAsync([Description("Optional Project ID")] string? projectId, [Description("The Task ID")] string taskId)
    {
        if (!EnsureAuthenticated("get_task", out var err)) return err!;
        if (string.IsNullOrEmpty(taskId))
            return FormatError("TASK_NOT_FOUND", "taskId argument is required");

        ProjectPlan? plan = null;
        if (!string.IsNullOrEmpty(projectId))
        {
            plan = await _planningService.GetPlanAsync(projectId);
        }
        else
        {
            var plans = await _planningService.GetProjectsAsync();
            plan = plans.FirstOrDefault();
        }

        if (plan == null)
            return FormatError("PROJECT_NOT_FOUND", "Project not found");

        TaskPlan? taskPlan = null;
        foreach (var phase in plan.Phases)
        {
            taskPlan = phase.Tasks.FirstOrDefault(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
            if (taskPlan != null) break;
        }

        if (taskPlan == null)
            return FormatError("TASK_NOT_FOUND", $"Task '{taskId}' not found");

        return FormatResult(new
        {
            taskId = taskPlan.TaskId,
            phaseId = taskPlan.PhaseId,
            title = taskPlan.Title,
            objective = taskPlan.Objective,
            status = taskPlan.Status.ToString(),
            dependencies = taskPlan.Dependencies,
            acceptanceCriteria = taskPlan.AcceptanceCriteria,
            estimatedComplexity = taskPlan.EstimatedComplexity,
            retryCount = taskPlan.RetryCount,
            maxRetries = taskPlan.MaxRetries,
            requiresHumanApproval = taskPlan.RequiresHumanApproval
        });
    }

    [McpServerTool(Name = "prepare_task_execution")]
    [Description("Generate an authoritative ExecutionPromptPackage for a task with external ChatGPT instructions. Does not modify the project plan.")]
    public async Task<string> PrepareTaskExecutionAsync(
        string projectId,
        string phaseId,
        string taskId,
        [Description("ChatGPT execution prompt instructions")] string externalInstructions,
        [Description("Optional verification instructions")] string? verificationInstructions = null,
        [Description("Optional commit message instructions")] string? commitInstructions = null)
    {
        if (!EnsureAuthenticated("prepare_task_execution", out var err)) return err!;
        if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(phaseId) || string.IsNullOrEmpty(taskId))
            return FormatError("TASK_NOT_FOUND", "projectId, phaseId, and taskId arguments are required");

        var plan = await _planningService.GetPlanAsync(projectId);
        if (plan == null)
            return FormatError("PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

        var cfg = _configService.LoadConfig();
        string workspacePath = cfg.ProjectWorkspaces.TryGetValue(projectId, out var ws) ? ws : cfg.WorkspacePath;
        List<string>? verList = !string.IsNullOrEmpty(verificationInstructions)
            ? new List<string> { verificationInstructions }
            : null;

        var pkg = await _executionPromptService.PreparePromptPackageAsync(
            plan, phaseId, taskId, externalInstructions ?? "", verList, commitInstructions ?? "", workspacePath, generatedBy: "ChatGPTWeb");

        return FormatResult(pkg);
    }

    [McpServerTool(Name = "dispatch_task")]
    [Description("Dispatch a prepared task prompt to the configured coding agent for execution. Uses trusted pre-configured workspace path. Does not approve Human Gates. Does not mark a task Passed.")]
    public async Task<string> DispatchTaskAsync(
        string projectId,
        string phaseId,
        string taskId,
        string promptId,
        [Description("Optional execution timeout in seconds")] int? timeoutSeconds = 600)
    {
        if (!EnsureAuthenticated("dispatch_task", out var err)) return err!;
        if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(phaseId) || string.IsNullOrEmpty(taskId))
            return FormatError("TASK_NOT_FOUND", "projectId, phaseId, and taskId arguments are required");

        var plan = await _planningService.GetPlanAsync(projectId);
        if (plan == null)
            return FormatError("PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

        var cfg = _configService.LoadConfig();
        string workspacePath = cfg.ProjectWorkspaces.TryGetValue(projectId, out var ws) ? ws : cfg.WorkspacePath;

        var currentActive = _codingAgentService.GetCurrentExecution();
        if (currentActive != null)
        {
            if (string.Equals(currentActive.PromptId, promptId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentActive.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
            {
                return FormatError("ALREADY_RUNNING", $"Execution is already running for task '{taskId}'. ExecutionId: {currentActive.ExecutionId}");
            }
        }

        ExecutionPromptPackage? promptPkg = null;
        if (!string.IsNullOrEmpty(promptId))
        {
            promptPkg = _executionPromptService.GetPromptPackage(promptId);
        }

        if (promptPkg == null)
        {
            promptPkg = await _executionPromptService.PreparePromptPackageAsync(plan, phaseId, taskId, workspacePath: workspacePath, generatedBy: "ChatGPTWeb");
        }

        var result = await _codingAgentService.DispatchTaskAsync(plan, phaseId, taskId, promptPkg, workspacePath, timeoutSeconds ?? 600);

        if (string.IsNullOrEmpty(result.ExecutionId))
        {
            return FormatError(string.IsNullOrEmpty(result.ErrorCode) ? "DISPATCH_FAILED" : result.ErrorCode, result.ErrorMessage ?? "Dispatch failed");
        }

        return FormatResult(new
        {
            success = result.Success,
            executionId = result.ExecutionId,
            agentTaskId = result.AgentTaskId,
            exitCode = result.ExitCode,
            agentSuccess = result.Success,
            errorCode = result.ErrorCode,
            errorMessage = result.ErrorMessage,
            status = "Reviewing"
        });
    }

    [McpServerTool(Name = "get_current_execution")]
    [Description("Get real-time execution status of the currently running coding agent task.")]
    public string GetCurrentExecution()
    {
        if (!EnsureAuthenticated("get_current_execution", out var err)) return err!;
        var rec = _codingAgentService.GetCurrentExecution();
        if (rec == null)
        {
            return FormatResult(new { isRunning = false, message = "No active execution" });
        }
        return FormatResult(rec);
    }

    [McpServerTool(Name = "get_execution")]
    [Description("Get detailed record of a specific execution attempt by ExecutionId.")]
    public string GetExecution([Description("The Execution ID")] string executionId)
    {
        if (!EnsureAuthenticated("get_execution", out var err)) return err!;
        if (string.IsNullOrEmpty(executionId))
            return FormatError("EXECUTION_NOT_FOUND", "executionId argument is required");

        var rec = _codingAgentService.GetExecutionHistory().FirstOrDefault(r => r.ExecutionId == executionId || r.AgentTaskId == executionId)
                  ?? _taskRegistry.GetRecord(executionId);

        if (rec == null)
            return FormatError("EXECUTION_NOT_FOUND", $"Execution '{executionId}' not found");

        return FormatResult(rec);
    }

    [McpServerTool(Name = "cancel_execution")]
    [Description("Cancel the active coding agent execution attempt.")]
    public string CancelExecution([Description("The Execution ID")] string executionId)
    {
        if (!EnsureAuthenticated("cancel_execution", out var err)) return err!;
        _codingAgentService.CancelCurrentExecution();
        return FormatResult(new { success = true, message = "Cancellation requested for execution " + executionId });
    }

    [McpServerTool(Name = "get_review_package")]
    [Description("Retrieve the ExecutionReviewPackage for an execution, including diffs, changed files, criteria, and stderr. Exit code 0 alone does not mean Task Passed. ReviewStatus remains Pending.")]
    public string GetReviewPackage([Description("The Execution ID")] string executionId)
    {
        if (!EnsureAuthenticated("get_review_package", out var err)) return err!;
        if (string.IsNullOrEmpty(executionId))
            return FormatError("EXECUTION_NOT_FOUND", "executionId argument is required");

        var pkg = _codingAgentService.GetReviewPackage(executionId);
        if (pkg == null)
            return FormatError("EXECUTION_NOT_FOUND", $"Execution review package not found for '{executionId}'");

        return FormatResult(pkg);
    }

    [McpServerTool(Name = "get_git_evidence")]
    [Description("Retrieve exact Git diff evidence generated by a specific execution attempt.")]
    public string GetGitEvidence([Description("The Execution ID")] string executionId)
    {
        if (!EnsureAuthenticated("get_git_evidence", out var err)) return err!;
        if (string.IsNullOrEmpty(executionId))
            return FormatError("EXECUTION_NOT_FOUND", "executionId argument is required");

        var evidence = _taskRegistry.GetGitEvidence(executionId)
                       ?? _codingAgentService.GetReviewPackage(executionId)?.GitEvidence;

        if (evidence == null)
            return FormatError("GIT_EVIDENCE_UNAVAILABLE", $"Git evidence unavailable for execution '{executionId}'");

        return FormatResult(evidence);
    }
}
