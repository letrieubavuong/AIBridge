using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIBridge.Api.Dtos;
using AIBridge.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIBridge.Services;

public class BridgeServer : IBridgeServer
{
    private readonly IConfigService _configService;
    private readonly ILogService _logService;
    private readonly ITaskService _taskService;
    private readonly ITaskRegistry _taskRegistry;
    private readonly IAntigravityEnvironmentService _environmentService;
    private readonly ICodingAgentRunner _antigravityRunner;
    private readonly IGitEnvironmentService _gitEnvironmentService;
    private readonly IGitCommandService _gitCommandService;
    private readonly IGitEvidenceService _gitEvidenceService;

    private WebApplication? _app;
    private BridgeStatus _status = BridgeStatus.Stopped;
    private readonly object _statusLock = new();

    private const int MaxRequestSizeBytes = 262144; // 256 KB
    private const int MaxLogOutputChars = 65536;    // 64 KB

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public event Action<BridgeStatus>? StatusChanged;

    public BridgeStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
        private set
        {
            lock (_statusLock)
            {
                if (_status != value)
                {
                    _status = value;
                    StatusChanged?.Invoke(_status);
                }
            }
        }
    }

    public string Host => _configService.LoadConfig().BridgeHost;
    public int Port => _configService.LoadConfig().BridgePort;

    public BridgeServer(
        IConfigService configService,
        ILogService logService,
        ITaskService taskService,
        ITaskRegistry taskRegistry,
        IAntigravityEnvironmentService environmentService,
        ICodingAgentRunner antigravityRunner,
        IGitEnvironmentService? gitEnvironmentService = null,
        IGitCommandService? gitCommandService = null,
        IGitEvidenceService? gitEvidenceService = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _taskRegistry = taskRegistry ?? throw new ArgumentNullException(nameof(taskRegistry));
        _environmentService = environmentService ?? throw new ArgumentNullException(nameof(environmentService));
        _antigravityRunner = antigravityRunner ?? throw new ArgumentNullException(nameof(antigravityRunner));

        _gitCommandService = gitCommandService ?? new GitCommandService(_logService);
        _gitEnvironmentService = gitEnvironmentService ?? new GitEnvironmentService(_logService, _gitCommandService);
        _gitEvidenceService = gitEvidenceService ?? new GitEvidenceService(_logService, _gitEnvironmentService, _gitCommandService);
    }

    public async Task<bool> StartAsync()
    {
        if (Status == BridgeStatus.Running || Status == BridgeStatus.Starting)
        {
            _logService.LogWarning($"BridgeServer is already {Status}.");
            return Status == BridgeStatus.Running;
        }

        Status = BridgeStatus.Starting;
        var config = _configService.LoadConfig();

        // Validate Host security boundary (127.0.0.1 only for Phase 03)
        var host = string.IsNullOrWhiteSpace(config.BridgeHost) ? "127.0.0.1" : config.BridgeHost.Trim();
        if (host is "0.0.0.0" or "::" or "*")
        {
            _logService.LogError($"Bridge: ERROR Unsafe bind address '{host}' rejected. Phase 03 allows localhost only (127.0.0.1).");
            Status = BridgeStatus.Error;
            return false;
        }

        var port = config.BridgePort;
        if (port is < 1 or > 65535)
        {
            _logService.LogError($"Bridge: ERROR Invalid port {port}. Port must be between 1 and 65535.");
            Status = BridgeStatus.Error;
            return false;
        }

        // Ensure API Token exists
        if (string.IsNullOrWhiteSpace(config.ApiToken))
        {
            config.ApiToken = GenerateSecureToken();
            _configService.SaveConfig(config);
            _logService.LogInfo("Generated new secure API authentication token.");
        }

        _logService.LogInfo($"Bridge starting on {host}:{port}...");

        try
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });

            builder.WebHost.UseKestrel(options =>
            {
                options.Listen(IPAddress.Parse(host), port);
                options.Limits.MaxRequestBodySize = MaxRequestSizeBytes;
            });

            builder.Logging.ClearProviders();
            builder.Services.AddRouting();

            var app = builder.Build();

            // Request size enforcement middleware
            app.Use(async (context, next) =>
            {
                if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > MaxRequestSizeBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        Error = "REQUEST_TOO_LARGE",
                        Message = "Request body exceeds maximum allowed size of 256 KB."
                    }, JsonOptions);
                    return;
                }
                await next();
            });

            // Local API Security Middleware
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? string.Empty;

                // Health endpoint is unauthenticated
                if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
                {
                    await next();
                    return;
                }

                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    var currentConfig = _configService.LoadConfig();
                    var token = ExtractToken(context.Request);

                    if (string.IsNullOrEmpty(token) || !ValidateToken(token, currentConfig.ApiToken))
                    {
                        _logService.LogWarning($"Bridge API: Unauthorized request to '{path}' rejected.");
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            Error = "UNAUTHORIZED",
                            Message = "Invalid or missing API token."
                        }, JsonOptions);
                        return;
                    }
                }

                await next();
            });

            // Map Endpoints
            MapEndpoints(app);

            _app = app;
            await app.StartAsync();

            Status = BridgeStatus.Running;
            _logService.LogInfo($"Bridge started on http://{host}:{port}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            _logService.LogError($"Bridge: ERROR {host}:{port} Port already in use or socket error.", ex);
            Status = BridgeStatus.Error;
            return false;
        }
        catch (Exception ex)
        {
            _logService.LogError($"Failed to start BridgeServer on {host}:{port}", ex);
            Status = BridgeStatus.Error;
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (Status == BridgeStatus.Stopped || Status == BridgeStatus.Stopping)
        {
            return;
        }

        Status = BridgeStatus.Stopping;
        _logService.LogInfo("Bridge stopping...");

        try
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Error stopping BridgeServer", ex);
        }
        finally
        {
            Status = BridgeStatus.Stopped;
            _logService.LogInfo("Bridge stopped.");
        }
    }

    private void MapEndpoints(WebApplication app)
    {
        // GET /api/health
        app.MapGet("/api/health", () =>
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";

            return Results.Ok(new HealthResponse
            {
                Status = "ok",
                Bridge = Status.ToString().ToLowerInvariant(),
                Version = versionStr,
                MachineName = Environment.MachineName,
                Timestamp = DateTime.UtcNow.ToString("o")
            });
        });

        // GET /api/environment
        app.MapGet("/api/environment", async () =>
        {
            var config = _configService.LoadConfig();
            var envInfo = await _environmentService.DetectAndVerifyEnvironmentAsync(config.AntigravityPath);

            return Results.Ok(new EnvironmentResponse
            {
                Antigravity = new AntigravityInfoResponse
                {
                    InstallationState = envInfo.InstallationState.ToString(),
                    Version = envInfo.Version,
                    AuthenticationState = envInfo.AuthState.ToString()
                },
                WorkspaceConfigured = !string.IsNullOrWhiteSpace(config.WorkspacePath) && Directory.Exists(config.WorkspacePath),
                Bridge = new BridgeInfoResponse
                {
                    Host = config.BridgeHost,
                    Port = config.BridgePort
                }
            });
        });

        // POST /api/tasks
        app.MapPost("/api/tasks", async (HttpContext context) =>
        {
            SubmitTaskRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<SubmitTaskRequest>(JsonOptions);
            }
            catch
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "INVALID_JSON",
                    Message = "Malformed JSON request body."
                }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Prompt))
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "EMPTY_PROMPT",
                    Message = "Prompt must not be empty or whitespace."
                }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (string.IsNullOrWhiteSpace(request.WorkspacePath) || !Directory.Exists(request.WorkspacePath))
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "INVALID_WORKSPACE",
                    Message = "Workspace path is missing or directory does not exist."
                }, statusCode: StatusCodes.Status400BadRequest);
            }

            // Atomic single active task admission check
            if (!_taskService.TryAcquireExecutionSlot())
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "TASK_ALREADY_RUNNING",
                    Message = "A task is already running."
                }, statusCode: StatusCodes.Status409Conflict);
            }

            var config = _configService.LoadConfig();
            var envInfo = await _environmentService.DetectAndVerifyEnvironmentAsync(config.AntigravityPath);

            if (envInfo.InstallationState != CliInstallationState.Ready)
            {
                _taskService.ReleaseExecutionSlot();
                return Results.Json(new ErrorResponse
                {
                    Error = "ANTIGRAVITY_UNAVAILABLE",
                    Message = "Antigravity CLI is not installed or unavailable."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (envInfo.AuthState != CliAuthState.Ready)
            {
                _taskService.ReleaseExecutionSlot();
                return Results.Json(new ErrorResponse
                {
                    Error = "ANTIGRAVITY_AUTH_REQUIRED",
                    Message = "Antigravity CLI authentication is required before tasks can be executed."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var task = _taskService.CreateTask(request.Prompt, request.WorkspacePath, config.AntigravityPath);
            _taskRegistry.RegisterTask(task);

            _logService.LogInfo($"Bridge API: Accepted task {task.Id}. Launching execution background worker...");

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _taskService.SubmitTaskAsync(task, _antigravityRunner);
                    _taskRegistry.RecordResult(task.Id, result);
                }
                catch (Exception ex)
                {
                    _logService.LogError($"Background task execution exception for task {task.Id}", ex);
                }
            });

            return Results.Json(new SubmitTaskResponse
            {
                TaskId = task.Id,
                Status = task.Status.ToString()
            }, statusCode: StatusCodes.Status202Accepted);
        });

        // GET /api/tasks/current
        app.MapGet("/api/tasks/current", () =>
        {
            var currentRecord = _taskRegistry.GetCurrentRecord();
            if (currentRecord == null)
            {
                var taskFromService = _taskService.CurrentTask;
                if (taskFromService == null)
                {
                    return Results.Ok(new { taskId = (string?)null, status = "Idle" });
                }
                return Results.Ok(BuildTaskStatusResponse(taskFromService, null));
            }

            return Results.Ok(BuildTaskStatusResponse(currentRecord.Task, currentRecord.Result));
        });

        // GET /api/tasks/{taskId}
        app.MapGet("/api/tasks/{taskId}", (string taskId) =>
        {
            var record = _taskRegistry.GetRecord(taskId);
            if (record == null)
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "TASK_NOT_FOUND",
                    Message = $"Task '{taskId}' was not found."
                }, statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok(BuildTaskStatusResponse(record.Task, record.Result));
        });

        // POST /api/tasks/{taskId}/cancel
        app.MapPost("/api/tasks/{taskId}/cancel", (string taskId) =>
        {
            var record = _taskRegistry.GetRecord(taskId);
            if (record == null)
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "TASK_NOT_FOUND",
                    Message = $"Task '{taskId}' was not found."
                }, statusCode: StatusCodes.Status404NotFound);
            }

            if (record.Task.Status != AgentTaskStatus.Running)
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "TASK_NOT_RUNNING",
                    Message = $"Task '{taskId}' is not currently running (Status: {record.Task.Status})."
                }, statusCode: StatusCodes.Status409Conflict);
            }

            _logService.LogInfo($"Bridge API: Cancel requested for task {taskId}.");
            _taskService.CancelCurrentTask();

            return Results.Json(new
            {
                taskId = taskId,
                status = "Cancelling"
            }, statusCode: StatusCodes.Status202Accepted);
        });

        // GET /api/logs
        app.MapGet("/api/logs", (int? limit) =>
        {
            var maxCount = Math.Clamp(limit ?? 100, 1, 500);
            var logs = _logService.GetRecentLogs(maxCount)
                .Select(l => new
                {
                    timestamp = l.Timestamp.ToString("o"),
                    level = l.Level.ToString(),
                    message = l.Message
                });

            return Results.Ok(logs);
        });

        // GET /api/git/environment
        app.MapGet("/api/git/environment", async (string? workspacePath) =>
        {
            var config = _configService.LoadConfig();
            var targetWorkspace = string.IsNullOrWhiteSpace(workspacePath) ? config.WorkspacePath : workspacePath;
            var gitEnv = await _gitEnvironmentService.DetectAndVerifyEnvironmentAsync(targetWorkspace, config.GitPath);

            return Results.Ok(new
            {
                gitInstalled = gitEnv.GitInstalled,
                version = gitEnv.Version,
                gitPath = gitEnv.GitPath,
                isRepository = gitEnv.IsRepository,
                repositoryRoot = gitEnv.RepositoryRoot,
                branch = gitEnv.Branch,
                remoteConfigured = gitEnv.RemoteConfigured,
                remoteName = gitEnv.RemoteName,
                remoteUrlSafe = gitEnv.RemoteUrlSafe
            });
        });

        // GET /api/git/status
        app.MapGet("/api/git/status", async (string? workspacePath) =>
        {
            var config = _configService.LoadConfig();
            var targetWorkspace = string.IsNullOrWhiteSpace(workspacePath) ? config.WorkspacePath : workspacePath;
            var gitEnv = await _gitEnvironmentService.DetectAndVerifyEnvironmentAsync(targetWorkspace, config.GitPath);

            if (!gitEnv.GitInstalled)
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "GIT_NOT_INSTALLED",
                    Message = "Git CLI is not installed or available."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!gitEnv.IsRepository)
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "NOT_A_GIT_REPOSITORY",
                    Message = "Configured workspace is not a Git repository."
                }, statusCode: StatusCodes.Status400BadRequest);
            }

            var gitExe = gitEnv.GitPath;
            var repoRoot = gitEnv.RepositoryRoot;
            var headSha = await _gitCommandService.GetHeadShaAsync(repoRoot, gitExe) ?? string.Empty;
            var headShort = headSha.Length >= 7 ? headSha[..7] : headSha;
            var headMsg = await _gitCommandService.GetHeadMessageAsync(repoRoot, headSha, gitExe) ?? string.Empty;
            var (_, _, isDirty) = await _gitCommandService.GetStatusAsync(repoRoot, gitExe);
            var aheadBehind = await _gitCommandService.GetAheadBehindAsync(repoRoot, gitEnv.Branch, gitEnv.RemoteName, gitExe);

            string pushState;
            if (string.IsNullOrEmpty(gitEnv.RemoteName))
            {
                pushState = "NotConfigured";
            }
            else if (!aheadBehind.IsVerified)
            {
                pushState = "Unknown";
            }
            else if (aheadBehind.Ahead > 0)
            {
                pushState = "NotPushed";
            }
            else
            {
                pushState = "Pushed";
            }

            var repoInfo = new GitRepositoryInfo
            {
                IsGitRepository = true,
                RepositoryRoot = repoRoot,
                Branch = gitEnv.Branch,
                HeadSha = headSha,
                HeadShortSha = headShort,
                HeadMessage = headMsg,
                RemoteName = gitEnv.RemoteName,
                RemoteUrlSafe = gitEnv.RemoteUrlSafe,
                IsDirty = isDirty,
                Ahead = aheadBehind.Ahead,
                Behind = aheadBehind.Behind,
                PushState = pushState
            };

            return Results.Ok(repoInfo);
        });

        // GET /api/tasks/{taskId}/git
        app.MapGet("/api/tasks/{taskId}/git", (string taskId) =>
        {
            var record = _taskRegistry.GetRecord(taskId);
            if (record == null)
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "TASK_NOT_FOUND",
                    Message = $"Task '{taskId}' was not found."
                }, statusCode: StatusCodes.Status404NotFound);
            }

            if (record.GitEvidence == null)
            {
                return Results.Ok(new
                {
                    taskId = taskId,
                    status = record.Task.Status == AgentTaskStatus.Running ? "Pending" : "Unavailable"
                });
            }

            return Results.Ok(record.GitEvidence);
        });

        // POST /api/tasks/{taskId}/git/push
        app.MapPost("/api/tasks/{taskId}/git/push", async (string taskId) =>
        {
            var record = _taskRegistry.GetRecord(taskId);
            if (record == null)
            {
                return Results.Json(new ErrorResponse
                {
                    Error = "TASK_NOT_FOUND",
                    Message = $"Task '{taskId}' was not found."
                }, statusCode: StatusCodes.Status404NotFound);
            }

            var config = _configService.LoadConfig();
            var (success, pushState, message) = await _gitEvidenceService.PushTaskCommitAsync(taskId, record.Task.WorkspacePath, config);

            if (success)
            {
                if (record.GitEvidence != null)
                {
                    record.GitEvidence.PushState = "Pushed";
                }
                return Results.Ok(new
                {
                    taskId = taskId,
                    pushState = pushState,
                    message = message
                });
            }
            else
            {
                int statusCode = pushState switch
                {
                    "PUSH_REQUIRES_APPROVAL" => StatusCodes.Status403Forbidden,
                    "NO_COMMIT_TO_PUSH" => StatusCodes.Status400BadRequest,
                    "NOT_A_GIT_REPOSITORY" => StatusCodes.Status400BadRequest,
                    "GIT_NOT_AVAILABLE" => StatusCodes.Status503ServiceUnavailable,
                    "GIT_AUTH_REQUIRED" => StatusCodes.Status401Unauthorized,
                    _ => StatusCodes.Status500InternalServerError
                };

                return Results.Json(new ErrorResponse
                {
                    Error = pushState,
                    Message = message
                }, statusCode: statusCode);
            }
        });
    }

    private TaskStatusResponse BuildTaskStatusResponse(AgentTask task, AgentResult? result)
    {
        string? stdout = result?.StandardOutput;
        string? stderr = result?.StandardError;
        bool stdoutTruncated = false;
        bool stderrTruncated = false;

        if (stdout != null && stdout.Length > MaxLogOutputChars)
        {
            stdout = stdout[..MaxLogOutputChars] + "\n[... TRUNCATED AT 64KB ...]";
            stdoutTruncated = true;
        }

        if (stderr != null && stderr.Length > MaxLogOutputChars)
        {
            stderr = stderr[..MaxLogOutputChars] + "\n[... TRUNCATED AT 64KB ...]";
            stderrTruncated = true;
        }

        return new TaskStatusResponse
        {
            TaskId = task.Id,
            Status = task.Status.ToString(),
            Prompt = task.Prompt,
            WorkspacePath = task.WorkspacePath,
            CreatedAt = task.CreatedAt.ToString("o"),
            StartedAt = task.StartedAt?.ToString("o"),
            CompletedAt = task.CompletedAt?.ToString("o"),
            ExitCode = result?.ExitCode,
            ErrorMessage = result?.ErrorMessage,
            Stdout = stdout,
            Stderr = stderr,
            StdoutTruncated = stdoutTruncated,
            StderrTruncated = stderrTruncated
        };
    }

    private static string ExtractToken(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var str = authHeader.ToString();
            if (str.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return str["Bearer ".Length..].Trim();
            }
        }

        if (request.Headers.TryGetValue("X-AIBridge-Token", out var customHeader))
        {
            return customHeader.ToString().Trim();
        }

        return string.Empty;
    }

    private static bool ValidateToken(string inputToken, string configToken)
    {
        if (string.IsNullOrEmpty(inputToken) || string.IsNullOrEmpty(configToken))
        {
            return false;
        }

        var inputBytes = Encoding.UTF8.GetBytes(inputToken);
        var configBytes = Encoding.UTF8.GetBytes(configToken);

        if (inputBytes.Length != configBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(inputBytes, configBytes);
    }

    public static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
