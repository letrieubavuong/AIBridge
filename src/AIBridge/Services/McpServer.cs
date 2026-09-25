using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _isRunning;
    private readonly object _lock = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SseSession> _sessions = new();

    public event EventHandler<string>? LogMessage;
    public event EventHandler<bool>? StatusChanged;

    public bool IsRunning => _isRunning;
    public string BindAddress { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 8788;
    public string EndpointUrl => $"http://{BindAddress}:{Port}/mcp";

    private class SseSession
    {
        public string SessionId { get; }
        public Stream OutputStream { get; }
        public HttpListenerResponse Response { get; }
        public CancellationTokenSource Cts { get; }
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public SseSession(string sessionId, Stream outputStream, HttpListenerResponse response, CancellationTokenSource cts)
        {
            SessionId = sessionId;
            OutputStream = outputStream;
            Response = response;
            Cts = cts;
        }

        public async Task SendEventAsync(string eventName, string data)
        {
            await _writeLock.WaitAsync();
            try
            {
                string payload = $"event: {eventName}\ndata: {data}\n\n";
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                await OutputStream.WriteAsync(bytes);
                await OutputStream.FlushAsync();
            }
            catch
            {
                Cts.Cancel();
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

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

    public Task<bool> StartAsync()
    {
        lock (_lock)
        {
            if (_isRunning) return Task.FromResult(true);

            var cfg = _configService.LoadConfig();
            BindAddress = string.IsNullOrWhiteSpace(cfg.McpHost) ? "127.0.0.1" : cfg.McpHost;
            Port = cfg.McpPort > 0 ? cfg.McpPort : 8788;

            try
            {
                _listener = new HttpListener();
                string prefix = $"http://{BindAddress}:{Port}/";
                _listener.Prefixes.Add(prefix);
                _listener.Start();

                _cts = new CancellationTokenSource();
                _isRunning = true;
                _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));

                Log("MCP Server started at " + EndpointUrl);
                StatusChanged?.Invoke(this, true);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log("Failed to start MCP Server: " + ex.Message);
                _logService.LogError("MCP Server start error", ex);
                _listener?.Close();
                _listener = null;
                _isRunning = false;
                StatusChanged?.Invoke(this, false);
                return Task.FromResult(false);
            }
        }
    }

    public async Task StopAsync()
    {
        Task? taskToWait = null;
        lock (_lock)
        {
            if (!_isRunning) return;

            Log("Stopping MCP Server...");
            _cts?.Cancel();

            foreach (var kvp in _sessions)
            {
                try { kvp.Value.Cts.Cancel(); } catch { }
            }
            _sessions.Clear();

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }

            _listener = null;
            _isRunning = false;
            taskToWait = _listenTask;
            _listenTask = null;
        }

        if (taskToWait != null)
        {
            try
            {
                await taskToWait;
            }
            catch { }
        }

        StatusChanged?.Invoke(this, false);
        Log("MCP Server stopped.");
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    _logService.LogError("MCP listener loop error", ex);
                }
            }
        }
    }

    private static void ApplyCorsHeaders(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string? origin = req.Headers["Origin"];
        if (!string.IsNullOrWhiteSpace(origin))
        {
            resp.AddHeader("Access-Control-Allow-Origin", origin);
            resp.AddHeader("Access-Control-Allow-Credentials", "true");
        }
        else
        {
            resp.AddHeader("Access-Control-Allow-Origin", "*");
        }

        resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        resp.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, X-AIBridge-Token, Accept, mcp-session-id, Last-Event-ID");
        resp.AddHeader("Access-Control-Max-Age", "86400");
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;

        ApplyCorsHeaders(req, resp);

        if (req.HttpMethod == "OPTIONS")
        {
            resp.StatusCode = (int)HttpStatusCode.OK;
            resp.Close();
            return;
        }

        string rawPath = req.Url?.AbsolutePath ?? "/";
        string sessionId = req.QueryString["sessionId"] ?? req.Headers["mcp-session-id"] ?? string.Empty;

        // Handle GET requests (SSE / Health probe)
        if (req.HttpMethod == "GET")
        {
            bool isSseRequest = (req.AcceptTypes != null && req.AcceptTypes.Contains("text/event-stream")) ||
                                rawPath.EndsWith("/sse", StringComparison.OrdinalIgnoreCase) ||
                                rawPath.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase);

            if (isSseRequest)
            {
                await HandleSseConnectionAsync(req, resp);
                return;
            }

            // Plain GET health probe
            resp.ContentType = "application/json; charset=utf-8";
            resp.StatusCode = (int)HttpStatusCode.OK;
            var health = new { status = "ok", service = "AIBridge MCP Server", mcpReady = true, phase = "08" };
            byte[] healthBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(health, JsonOpts));
            await resp.OutputStream.WriteAsync(healthBytes);
            resp.Close();
            return;
        }

        if (req.HttpMethod != "POST")
        {
            resp.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            resp.Close();
            return;
        }

        // Check request size (max 256 KB)
        if (req.ContentLength64 > 256 * 1024)
        {
            Log("MCP request rejected: size exceeds 256 KB limit.");
            await WriteJsonRpcErrorAsync(resp, null, -32000, "REQUEST_TOO_LARGE: Request size exceeds maximum allowed 256 KB.", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        byte[] requestBuffer;
        using (var ms = new MemoryStream())
        {
            await req.InputStream.CopyToAsync(ms);
            requestBuffer = ms.ToArray();
        }

        if (requestBuffer.Length > 256 * 1024)
        {
            Log("MCP request payload exceeds 256 KB limit.");
            await WriteJsonRpcErrorAsync(resp, null, -32000, "REQUEST_TOO_LARGE: Payload size exceeds 256 KB.", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        string requestText = Encoding.UTF8.GetString(requestBuffer);

        // Authentication Check
        bool isAuthenticated = ValidateAuth(req);

        try
        {
            using var doc = JsonDocument.Parse(requestText);
            var root = doc.RootElement;

            object? responseObj = null;

            if (root.ValueKind == JsonValueKind.Array)
            {
                var batchResponses = new List<object>();
                foreach (var el in root.EnumerateArray())
                {
                    var singleResp = await ProcessJsonRpcRequestAsync(el, isAuthenticated);
                    if (singleResp != null) batchResponses.Add(singleResp);
                }
                responseObj = batchResponses;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                responseObj = await ProcessJsonRpcRequestAsync(root, isAuthenticated);
            }
            else
            {
                await WriteJsonRpcErrorAsync(resp, null, -32600, "Invalid Request", HttpStatusCode.BadRequest);
                return;
            }

            if (responseObj != null)
            {
                if (!string.IsNullOrEmpty(sessionId) && _sessions.TryGetValue(sessionId, out var session))
                {
                    string jsonPayload = JsonSerializer.Serialize(responseObj, JsonOpts);
                    _ = session.SendEventAsync("message", jsonPayload);
                }

                await WriteJsonResponseAsync(resp, responseObj, HttpStatusCode.OK);
            }
            else
            {
                resp.StatusCode = (int)HttpStatusCode.Accepted;
                resp.Close();
            }
        }
        catch (JsonException)
        {
            await WriteJsonRpcErrorAsync(resp, null, -32700, "Parse error", HttpStatusCode.BadRequest);
        }
        catch (Exception ex)
        {
            _logService.LogError("Error handling MCP request", ex);
            await WriteJsonRpcErrorAsync(resp, null, -32603, "INTERNAL_ERROR: " + ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    private async Task HandleSseConnectionAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string newSessionId = Guid.NewGuid().ToString("N");
        resp.ContentType = "text/event-stream";
        resp.StatusCode = (int)HttpStatusCode.OK;
        resp.AddHeader("Cache-Control", "no-cache");
        resp.AddHeader("Connection", "keep-alive");

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
        var session = new SseSession(newSessionId, resp.OutputStream, resp, sessionCts);
        _sessions[newSessionId] = session;

        Log($"MCP SSE Client connected. SessionId: {newSessionId}");

        try
        {
            string host = req.Headers["Host"] ?? $"{BindAddress}:{Port}";
            string scheme = req.IsSecureConnection ? "https" : "http";
            string endpointUrl = $"{scheme}://{host}/messages?sessionId={newSessionId}";

            await session.SendEventAsync("endpoint", endpointUrl);

            while (!sessionCts.Token.IsCancellationRequested && _isRunning)
            {
                await Task.Delay(15000, sessionCts.Token);
                await session.SendEventAsync("ping", "{}");
            }
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            _logService.LogWarning($"MCP SSE session '{newSessionId}' ended: {ex.Message}");
        }
        finally
        {
            _sessions.TryRemove(newSessionId, out _);
            Log($"MCP SSE Client disconnected. SessionId: {newSessionId}");
            try { resp.Close(); } catch { }
        }
    }

    private bool ValidateAuth(HttpListenerRequest req)
    {
        var cfg = _configService.LoadConfig();
        string expectedToken = cfg.ApiToken ?? string.Empty;

        // If no token is configured in system, allow
        if (string.IsNullOrEmpty(expectedToken)) return true;

        string? token = null;

        // 1. Check Authorization header
        string? authHeader = req.Headers["Authorization"];
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader.Substring(7).Trim();
        }

        // 2. Check X-AIBridge-Token header
        if (string.IsNullOrEmpty(token))
        {
            token = req.Headers["X-AIBridge-Token"];
        }

        // 3. Check query string
        if (string.IsNullOrEmpty(token))
        {
            token = req.QueryString["token"];
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

    private async Task<object?> ProcessJsonRpcRequestAsync(JsonElement root, bool isAuthenticated)
    {
        string? method = root.TryGetProperty("method", out var mProp) ? mProp.GetString() : null;
        object? id = null;
        if (root.TryGetProperty("id", out var idProp))
        {
            if (idProp.ValueKind == JsonValueKind.Number) id = idProp.GetInt64();
            else if (idProp.ValueKind == JsonValueKind.String) id = idProp.GetString();
        }

        if (string.IsNullOrEmpty(method))
        {
            return CreateJsonRpcError(id, -32600, "Invalid Request: method is missing");
        }

        Log($"MCP Received method '{method}'");

        // Handle MCP Protocol Base Methods
        if (method == "initialize")
        {
            return new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        tools = new { }
                    },
                    serverInfo = new
                    {
                        name = "AIBridge",
                        version = "0.8.0"
                    }
                }
            };
        }

        if (method == "notifications/initialized")
        {
            return null; // Notification -> no response body
        }

        if (method == "ping")
        {
            return new { jsonrpc = "2.0", id = id, result = new { } };
        }

        if (method == "tools/list")
        {
            return new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    tools = GetExposedToolsSchema()
                }
            };
        }

        if (method == "tools/call")
        {
            string? toolName = null;
            JsonElement argsElement = default;
            if (root.TryGetProperty("params", out var paramsEl))
            {
                if (paramsEl.TryGetProperty("name", out var nEl)) toolName = nEl.GetString();
                if (paramsEl.TryGetProperty("arguments", out var aEl)) argsElement = aEl;
            }

            if (string.IsNullOrEmpty(toolName))
            {
                return CreateJsonRpcError(id, -32602, "Invalid params: tool name is required");
            }

            // Authorization check for tool calls
            if (!isAuthenticated && toolName != "ping_bridge")
            {
                Log($"MCP tool call '{toolName}' rejected: UNAUTHORIZED.");
                return CreateJsonRpcError(id, -32001, "UNAUTHORIZED: Invalid or missing API token.");
            }

            Log($"MCP Executing Tool '{toolName}'");
            return await ExecuteToolAsync(id, toolName, argsElement);
        }

        return CreateJsonRpcError(id, -32601, $"Method not found: {method}");
    }

    private async Task<object> ExecuteToolAsync(object? id, string toolName, JsonElement args)
    {
        try
        {
            var cfg = _configService.LoadConfig();

            switch (toolName)
            {
                case "ping_bridge":
                    return FormatToolResult(id, new
                    {
                        ok = true,
                        service = "AIBridge",
                        phase = "08",
                        version = "0.8.0",
                        mcpReady = true
                    });

                case "get_bridge_status":
                    {
                        var plans = await _planningService.GetProjectsAsync();
                        var activePlan = plans.FirstOrDefault();
                        var currentTask = _taskService.CurrentTask;
                        var status = new
                        {
                            version = "0.8.0",
                            restStatus = "RUNNING (port 8787)",
                            mcpStatus = $"RUNNING (port {Port})",
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
                        return FormatToolResult(id, status);
                    }

                case "list_projects":
                    {
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
                        return FormatToolResult(id, list);
                    }

                case "get_project":
                    {
                        string? projectId = GetStringArg(args, "projectId");
                        if (string.IsNullOrEmpty(projectId))
                            return FormatToolError(id, "PROJECT_NOT_FOUND", "projectId argument is required");

                        var plan = await _planningService.GetPlanAsync(projectId);
                        if (plan == null)
                            return FormatToolError(id, "PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

                        var prog = _planningService.CalculateProjectProgress(plan);
                        return FormatToolResult(id, new
                        {
                            project = plan,
                            progress = prog
                        });
                    }

                case "get_project_progress":
                    {
                        string? projectId = GetStringArg(args, "projectId");
                        if (string.IsNullOrEmpty(projectId))
                            return FormatToolError(id, "PROJECT_NOT_FOUND", "projectId argument is required");

                        var plan = await _planningService.GetPlanAsync(projectId);
                        if (plan == null)
                            return FormatToolError(id, "PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

                        var prog = _planningService.CalculateProjectProgress(plan);
                        var currentPhase = plan.Phases.FirstOrDefault(p => p.PhaseNumber == prog.CurrentPhaseNumber);
                        var phaseProg = currentPhase != null ? _planningService.CalculatePhaseProgress(currentPhase) : null;

                        int passedTasks = plan.Phases.Sum(p => p.Tasks.Count(t => t.Status == TaskPlanStatus.Passed));
                        int totalTasks = plan.Phases.Sum(p => p.Tasks.Count);
                        string currentTaskId = plan.Phases.SelectMany(p => p.Tasks).FirstOrDefault(t => t.Status == TaskPlanStatus.Running || t.Status == TaskPlanStatus.Reviewing || t.Status == TaskPlanStatus.NotStarted)?.TaskId ?? string.Empty;

                        return FormatToolResult(id, new
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

                case "get_current_phase":
                    {
                        string? projectId = GetStringArg(args, "projectId");
                        if (string.IsNullOrEmpty(projectId))
                            return FormatToolError(id, "PROJECT_NOT_FOUND", "projectId argument is required");

                        var plan = await _planningService.GetPlanAsync(projectId);
                        if (plan == null)
                            return FormatToolError(id, "PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

                        var prog = _planningService.CalculateProjectProgress(plan);
                        var currentPhase = plan.Phases.FirstOrDefault(p => p.PhaseNumber == prog.CurrentPhaseNumber)
                                           ?? plan.Phases.FirstOrDefault(p => p.Status == PhaseStatus.Running)
                                           ?? plan.Phases.FirstOrDefault();

                        if (currentPhase == null)
                            return FormatToolError(id, "PHASE_NOT_FOUND", "No phase found in project");

                        return FormatToolResult(id, currentPhase);
                    }

                case "get_dispatchable_tasks":
                    {
                        string? projectId = GetStringArg(args, "projectId");
                        if (string.IsNullOrEmpty(projectId))
                            return FormatToolError(id, "PROJECT_NOT_FOUND", "projectId argument is required");

                        var plan = await _planningService.GetPlanAsync(projectId);
                        if (plan == null)
                            return FormatToolError(id, "PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

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

                        return FormatToolResult(id, resultList);
                    }

                case "get_task":
                    {
                        string? projectId = GetStringArg(args, "projectId");
                        string? taskId = GetStringArg(args, "taskId");

                        if (string.IsNullOrEmpty(taskId))
                            return FormatToolError(id, "TASK_NOT_FOUND", "taskId argument is required");

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
                            return FormatToolError(id, "PROJECT_NOT_FOUND", "Project not found");

                        TaskPlan? taskPlan = null;
                        foreach (var phase in plan.Phases)
                        {
                            taskPlan = phase.Tasks.FirstOrDefault(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
                            if (taskPlan != null) break;
                        }

                        if (taskPlan == null)
                            return FormatToolError(id, "TASK_NOT_FOUND", $"Task '{taskId}' not found");

                        return FormatToolResult(id, new
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

                case "prepare_task_execution":
                    {
                        string? projectId = GetStringArg(args, "projectId");
                        string? phaseId = GetStringArg(args, "phaseId");
                        string? taskId = GetStringArg(args, "taskId");
                        string? externalInstructions = GetStringArg(args, "externalInstructions");
                        string? verificationInstructionsStr = GetStringArg(args, "verificationInstructions");
                        string? commitInstructions = GetStringArg(args, "commitInstructions");

                        if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(phaseId) || string.IsNullOrEmpty(taskId))
                            return FormatToolError(id, "TASK_NOT_FOUND", "projectId, phaseId, and taskId arguments are required");

                        var plan = await _planningService.GetPlanAsync(projectId);
                        if (plan == null)
                            return FormatToolError(id, "PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

                        string workspacePath = cfg.ProjectWorkspaces.TryGetValue(projectId, out var ws) ? ws : cfg.WorkspacePath;
                        List<string>? verificationInstructions = !string.IsNullOrEmpty(verificationInstructionsStr)
                            ? new List<string> { verificationInstructionsStr }
                            : null;

                        var pkg = await _executionPromptService.PreparePromptPackageAsync(
                            plan, phaseId, taskId, externalInstructions ?? "", verificationInstructions, commitInstructions ?? "", workspacePath, generatedBy: "ChatGPTWeb");

                        return FormatToolResult(id, pkg);
                    }

                case "dispatch_task":
                    {
                        string? projectId = GetStringArg(args, "projectId");
                        string? phaseId = GetStringArg(args, "phaseId");
                        string? taskId = GetStringArg(args, "taskId");
                        string? promptId = GetStringArg(args, "promptId");
                        int timeoutSeconds = GetIntArg(args, "timeoutSeconds") ?? 600;

                        if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(phaseId) || string.IsNullOrEmpty(taskId))
                            return FormatToolError(id, "TASK_NOT_FOUND", "projectId, phaseId, and taskId arguments are required");

                        var plan = await _planningService.GetPlanAsync(projectId);
                        if (plan == null)
                            return FormatToolError(id, "PROJECT_NOT_FOUND", $"Project '{projectId}' not found");

                        string workspacePath = cfg.ProjectWorkspaces.TryGetValue(projectId, out var ws) ? ws : cfg.WorkspacePath;

                        // Check active execution for duplicate dispatch protection
                        var currentActive = _codingAgentService.GetCurrentExecution();
                        if (currentActive != null)
                        {
                            if (string.Equals(currentActive.PromptId, promptId, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(currentActive.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
                            {
                                return FormatToolError(id, "ALREADY_RUNNING", $"Execution is already running for task '{taskId}'. ExecutionId: {currentActive.ExecutionId}");
                            }
                        }

                        ExecutionPromptPackage? promptPkg = null;
                        if (!string.IsNullOrEmpty(promptId))
                        {
                            promptPkg = _executionPromptService.GetPromptPackage(promptId);
                        }

                        if (promptPkg == null)
                        {
                            // Prepare default prompt package if not found by id
                            promptPkg = await _executionPromptService.PreparePromptPackageAsync(plan, phaseId, taskId, workspacePath: workspacePath, generatedBy: "ChatGPTWeb");
                        }

                        var result = await _codingAgentService.DispatchTaskAsync(plan, phaseId, taskId, promptPkg, workspacePath, timeoutSeconds);

                        if (string.IsNullOrEmpty(result.ExecutionId))
                        {
                            return FormatToolError(id, string.IsNullOrEmpty(result.ErrorCode) ? "DISPATCH_FAILED" : result.ErrorCode, result.ErrorMessage ?? "Dispatch failed");
                        }

                        return FormatToolResult(id, new
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

                case "get_current_execution":
                    {
                        var rec = _codingAgentService.GetCurrentExecution();
                        if (rec == null)
                        {
                            return FormatToolResult(id, new { isRunning = false, message = "No active execution" });
                        }

                        return FormatToolResult(id, rec);
                    }

                case "get_execution":
                    {
                        string? executionId = GetStringArg(args, "executionId");
                        if (string.IsNullOrEmpty(executionId))
                            return FormatToolError(id, "EXECUTION_NOT_FOUND", "executionId argument is required");

                        var rec = _codingAgentService.GetExecutionHistory().FirstOrDefault(r => r.ExecutionId == executionId || r.AgentTaskId == executionId)
                                  ?? _taskRegistry.GetRecord(executionId);

                        if (rec == null)
                            return FormatToolError(id, "EXECUTION_NOT_FOUND", $"Execution '{executionId}' not found");

                        return FormatToolResult(id, rec);
                    }

                case "cancel_execution":
                    {
                        string? executionId = GetStringArg(args, "executionId");
                        _codingAgentService.CancelCurrentExecution();
                        return FormatToolResult(id, new { success = true, message = "Cancellation requested for execution " + executionId });
                    }

                case "get_review_package":
                    {
                        string? executionId = GetStringArg(args, "executionId");
                        if (string.IsNullOrEmpty(executionId))
                            return FormatToolError(id, "EXECUTION_NOT_FOUND", "executionId argument is required");

                        var pkg = _codingAgentService.GetReviewPackage(executionId);
                        if (pkg == null)
                            return FormatToolError(id, "EXECUTION_NOT_FOUND", $"Execution review package not found for '{executionId}'");

                        return FormatToolResult(id, pkg);
                    }

                case "get_git_evidence":
                    {
                        string? executionId = GetStringArg(args, "executionId");
                        if (string.IsNullOrEmpty(executionId))
                            return FormatToolError(id, "EXECUTION_NOT_FOUND", "executionId argument is required");

                        var evidence = _taskRegistry.GetGitEvidence(executionId)
                                       ?? _codingAgentService.GetReviewPackage(executionId)?.GitEvidence;

                        if (evidence == null)
                            return FormatToolError(id, "GIT_EVIDENCE_UNAVAILABLE", $"Git evidence unavailable for execution '{executionId}'");

                        return FormatToolResult(id, evidence);
                    }

                default:
                    return FormatToolError(id, "TOOL_NOT_FOUND", $"Unknown tool: {toolName}");
            }
        }
        catch (Exception ex)
        {
            _logService.LogError($"Error executing MCP tool '{toolName}'", ex);
            return FormatToolError(id, "INTERNAL_ERROR", ex.Message);
        }
    }

    private static string? GetStringArg(JsonElement args, string paramName)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(paramName, out var prop))
        {
            return prop.GetString();
        }
        return null;
    }

    private static int? GetIntArg(JsonElement args, string paramName)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(paramName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int val))
            {
                return val;
            }
        }
        return null;
    }

    private static object GetExposedToolsSchema()
    {
        return new object[]
        {
            new {
                name = "ping_bridge",
                description = "Check connectivity and readiness of the AIBridge MCP server. Does not reveal secrets.",
                inputSchema = new { type = "object", properties = new { } }
            },
            new {
                name = "get_bridge_status",
                description = "Get operational status of AIBridge including workspace, Git, Antigravity CLI, and active execution status.",
                inputSchema = new { type = "object", properties = new { } }
            },
            new {
                name = "list_projects",
                description = "List project plan summaries available in AIBridge.",
                inputSchema = new { type = "object", properties = new { } }
            },
            new {
                name = "get_project",
                description = "Get full structured project plan details by ProjectId.",
                inputSchema = new {
                    type = "object",
                    properties = new { projectId = new { type = "string", description = "The Project ID" } },
                    required = new[] { "projectId" }
                }
            },
            new {
                name = "get_project_progress",
                description = "Get quantitative progress metrics for a project.",
                inputSchema = new {
                    type = "object",
                    properties = new { projectId = new { type = "string", description = "The Project ID" } },
                    required = new[] { "projectId" }
                }
            },
            new {
                name = "get_current_phase",
                description = "Get the authoritative active phase for a project.",
                inputSchema = new {
                    type = "object",
                    properties = new { projectId = new { type = "string", description = "The Project ID" } },
                    required = new[] { "projectId" }
                }
            },
            new {
                name = "get_dispatchable_tasks",
                description = "Get tasks eligible for execution under Phase 07 dispatch policy. Tasks requiring pending Human Gate approval return dispatchable=false with reason HUMAN_APPROVAL_REQUIRED.",
                inputSchema = new {
                    type = "object",
                    properties = new { projectId = new { type = "string", description = "The Project ID" } },
                    required = new[] { "projectId" }
                }
            },
            new {
                name = "get_task",
                description = "Get detailed specification of a single task.",
                inputSchema = new {
                    type = "object",
                    properties = new {
                        projectId = new { type = "string", description = "Optional Project ID" },
                        taskId = new { type = "string", description = "The Task ID" }
                    },
                    required = new[] { "taskId" }
                }
            },
            new {
                name = "prepare_task_execution",
                description = "Generate an authoritative ExecutionPromptPackage for a task with external ChatGPT instructions. Does not modify the project plan.",
                inputSchema = new {
                    type = "object",
                    properties = new {
                        projectId = new { type = "string" },
                        phaseId = new { type = "string" },
                        taskId = new { type = "string" },
                        externalInstructions = new { type = "string", description = "ChatGPT execution prompt instructions" },
                        verificationInstructions = new { type = "string", description = "Optional verification instructions" },
                        commitInstructions = new { type = "string", description = "Optional commit message instructions" }
                    },
                    required = new[] { "projectId", "phaseId", "taskId", "externalInstructions" }
                }
            },
            new {
                name = "dispatch_task",
                description = "Dispatch a prepared task prompt to the configured coding agent for execution. Uses trusted pre-configured workspace path. Does not approve Human Gates. Does not mark a task Passed.",
                inputSchema = new {
                    type = "object",
                    properties = new {
                        projectId = new { type = "string" },
                        phaseId = new { type = "string" },
                        taskId = new { type = "string" },
                        promptId = new { type = "string" },
                        timeoutSeconds = new { type = "integer", description = "Optional execution timeout in seconds" }
                    },
                    required = new[] { "projectId", "phaseId", "taskId", "promptId" }
                }
            },
            new {
                name = "get_current_execution",
                description = "Get real-time execution status of the currently running coding agent task.",
                inputSchema = new { type = "object", properties = new { } }
            },
            new {
                name = "get_execution",
                description = "Get detailed record of a specific execution attempt by ExecutionId.",
                inputSchema = new {
                    type = "object",
                    properties = new { executionId = new { type = "string" } },
                    required = new[] { "executionId" }
                }
            },
            new {
                name = "cancel_execution",
                description = "Cancel the active coding agent execution attempt.",
                inputSchema = new {
                    type = "object",
                    properties = new { executionId = new { type = "string" } },
                    required = new[] { "executionId" }
                }
            },
            new {
                name = "get_review_package",
                description = "Retrieve the ExecutionReviewPackage for an execution, including diffs, changed files, criteria, and stderr. Exit code 0 alone does not mean Task Passed. ReviewStatus remains Pending.",
                inputSchema = new {
                    type = "object",
                    properties = new { executionId = new { type = "string" } },
                    required = new[] { "executionId" }
                }
            },
            new {
                name = "get_git_evidence",
                description = "Retrieve exact Git diff evidence generated by a specific execution attempt.",
                inputSchema = new {
                    type = "object",
                    properties = new { executionId = new { type = "string" } },
                    required = new[] { "executionId" }
                }
            }
        };
    }

    private static object FormatToolResult(object? id, object resultData)
    {
        string jsonText = JsonSerializer.Serialize(resultData, JsonOpts);
        return new
        {
            jsonrpc = "2.0",
            id = id,
            result = new
            {
                content = new object[]
                {
                    new
                    {
                        type = "text",
                        text = jsonText
                    }
                }
            }
        };
    }

    private static object FormatToolError(object? id, string errorCode, string message)
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

        string jsonText = JsonSerializer.Serialize(new { success = false, errorCode = normCode, errorMessage = message }, JsonOpts);
        return new
        {
            jsonrpc = "2.0",
            id = id,
            result = new
            {
                content = new object[]
                {
                    new
                    {
                        type = "text",
                        text = jsonText
                    }
                },
                isError = true
            }
        };
    }

    private static object CreateJsonRpcError(object? id, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id = id,
            error = new
            {
                code = code,
                message = message
            }
        };
    }

    private static async Task WriteJsonResponseAsync(HttpListenerResponse resp, object data, HttpStatusCode statusCode)
    {
        resp.ContentType = "application/json; charset=utf-8";
        resp.StatusCode = (int)statusCode;
        string json = JsonSerializer.Serialize(data, JsonOpts);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    private static async Task WriteJsonRpcErrorAsync(HttpListenerResponse resp, object? id, int code, string message, HttpStatusCode statusCode)
    {
        var errObj = CreateJsonRpcError(id, code, message);
        await WriteJsonResponseAsync(resp, errObj, statusCode);
    }

    private void Log(string message)
    {
        _logService.LogInfo($"[MCP] {message}");
        LogMessage?.Invoke(this, message);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
