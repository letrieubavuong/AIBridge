using System.Diagnostics;
using System.IO;
using System.Text;
using AIBridge.Models;

namespace AIBridge.Services;

public class AntigravityRunner : ICodingAgentRunner
{
    private readonly ILogService _logService;
    private readonly IAntigravityEnvironmentService _environmentService;
    private readonly int _timeoutMinutes;

    public string Name => "Antigravity";

    public AntigravityRunner(
        ILogService logService, 
        IAntigravityEnvironmentService environmentService, 
        int timeoutMinutes = 30)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _environmentService = environmentService ?? throw new ArgumentNullException(nameof(environmentService));
        _timeoutMinutes = timeoutMinutes > 0 ? timeoutMinutes : 30;
    }

    public string ResolveCliPath(string? configuredPath)
    {
        return _environmentService.ResolveCliExecutable(configuredPath);
    }

    public async Task<(bool IsValid, string Message)> ValidateConfigurationAsync(string agentPath)
    {
        var resolvedCli = ResolveCliPath(agentPath);
        if (string.IsNullOrWhiteSpace(resolvedCli))
        {
            var msg = "Antigravity CLI ('agy.exe') not found in configured path, %LOCALAPPDATA%\\agy\\bin, or PATH.";
            _logService.LogWarning(msg);
            return (false, msg);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedCli,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(cts.Token);

            var versionOutput = (await stdoutTask).Trim();
            if (string.IsNullOrWhiteSpace(versionOutput))
            {
                versionOutput = (await stderrTask).Trim();
            }

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(versionOutput))
            {
                var msg = $"Antigravity CLI verified: agy {versionOutput} ({resolvedCli})";
                _logService.LogInfo(msg);
                return (true, msg);
            }
            else
            {
                var msg = $"Antigravity CLI health check failed with exit code {process.ExitCode}: {versionOutput}";
                _logService.LogWarning(msg);
                return (false, msg);
            }
        }
        catch (Exception ex)
        {
            var msg = $"Error validating Antigravity CLI at '{resolvedCli}': {ex.Message}";
            _logService.LogError(msg, ex);
            return (false, msg);
        }
    }

    public Task<(bool IsValid, string Message)> ValidateWorkspaceAsync(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            var msg = "Workspace path is empty.";
            _logService.LogWarning(msg);
            return Task.FromResult((false, msg));
        }

        try
        {
            var normalizedPath = Path.GetFullPath(workspacePath);
            if (Directory.Exists(normalizedPath))
            {
                var msg = $"Workspace path validated successfully: {normalizedPath}";
                _logService.LogInfo(msg);
                return Task.FromResult((true, msg));
            }
            else
            {
                var msg = $"Workspace directory does not exist: {normalizedPath}";
                _logService.LogWarning(msg);
                return Task.FromResult((false, msg));
            }
        }
        catch (Exception ex)
        {
            var msg = $"Error validating workspace path: {ex.Message}";
            _logService.LogError(msg, ex);
            return Task.FromResult((false, msg));
        }
    }

    public async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        var startTime = DateTime.Now;

        if (string.IsNullOrWhiteSpace(task.Prompt))
        {
            _logService.LogWarning("Task execution aborted: Prompt is empty.");
            return new AgentResult
            {
                Success = false,
                ExitCode = -1,
                StartedAt = startTime,
                CompletedAt = DateTime.Now,
                ErrorMessage = "Task prompt cannot be empty."
            };
        }

        if (string.IsNullOrWhiteSpace(task.WorkspacePath) || !Directory.Exists(task.WorkspacePath))
        {
            _logService.LogWarning($"Task execution aborted: Workspace directory '{task.WorkspacePath}' does not exist.");
            return new AgentResult
            {
                Success = false,
                ExitCode = -1,
                StartedAt = startTime,
                CompletedAt = DateTime.Now,
                ErrorMessage = $"Workspace directory '{task.WorkspacePath}' is invalid or does not exist."
            };
        }

        var normalizedWorkspace = Path.GetFullPath(task.WorkspacePath);
        
        // Single authoritative CLI path resolution using task configured path or environment service
        var cliPath = ResolveCliPath(task.ConfiguredAgentPath);

        if (string.IsNullOrWhiteSpace(cliPath))
        {
            _logService.LogError("Antigravity CLI ('agy.exe') could not be resolved.");
            return new AgentResult
            {
                Success = false,
                ExitCode = -1,
                StartedAt = startTime,
                CompletedAt = DateTime.Now,
                ErrorMessage = "Antigravity CLI executable ('agy.exe') not found in configured path, %LOCALAPPDATA%\\agy\\bin, or system PATH."
            };
        }

        _logService.LogInfo($"[INFO] Starting Antigravity CLI process...");
        _logService.LogInfo($"[INFO] CLI Executable: {cliPath}");
        _logService.LogInfo($"[INFO] Workspace: {normalizedWorkspace}");
        _logService.LogInfo($"[INFO] Timeout limit: {_timeoutMinutes} minute(s)");

        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = normalizedWorkspace
        };

        // Pass prompt with -p flag. NO --dangerously-skip-permissions!
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(task.Prompt);

        var stdOutBuilder = new StringBuilder();
        var stdErrBuilder = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (sender, args) =>
        {
            if (args.Data != null)
            {
                stdOutBuilder.AppendLine(args.Data);
                _logService.LogInfo($"[AGENT] {args.Data}");
            }
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (args.Data != null)
            {
                stdErrBuilder.AppendLine(args.Data);
                _logService.LogWarning($"[AGENT STDERR] {args.Data}");
            }
        };

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_timeoutMinutes));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var cancelRegistration = linkedCts.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }
        });

        try
        {
            linkedCts.Token.ThrowIfCancellationRequested();

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(linkedCts.Token);

            var endTime = DateTime.Now;
            var stdOut = stdOutBuilder.ToString();
            var stdErr = stdErrBuilder.ToString();
            var exitCode = process.ExitCode;

            _logService.LogInfo($"[INFO] Antigravity process exited with code {exitCode}.");

            // Permission Error Detection
            var combinedOutput = stdOut + "\n" + stdErr;
            if (IsPermissionDenied(combinedOutput))
            {
                var permErrorMsg = "PERMISSION_REQUIRED: Antigravity requires tool permission that cannot be interactively approved in headless mode. Review ~/.gemini/antigravity-cli/settings.json or grant required permissions.";
                _logService.LogError(permErrorMsg);
                return new AgentResult
                {
                    Success = false,
                    ExitCode = exitCode,
                    StartedAt = startTime,
                    CompletedAt = endTime,
                    StandardOutput = stdOut,
                    StandardError = stdErr,
                    ErrorMessage = permErrorMsg
                };
            }

            if (exitCode != 0)
            {
                var errMessage = $"Antigravity process exited with non-zero exit code: {exitCode}";
                return new AgentResult
                {
                    Success = false,
                    ExitCode = exitCode,
                    StartedAt = startTime,
                    CompletedAt = endTime,
                    StandardOutput = stdOut,
                    StandardError = stdErr,
                    ErrorMessage = errMessage
                };
            }

            return new AgentResult
            {
                Success = true,
                ExitCode = 0,
                StartedAt = startTime,
                CompletedAt = endTime,
                StandardOutput = stdOut,
                StandardError = stdErr,
                ErrorMessage = string.Empty
            };
        }
        catch (OperationCanceledException)
        {
            var endTime = DateTime.Now;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }

            // Differentiate User Cancellation vs Timeout
            if (cancellationToken.IsCancellationRequested)
            {
                _logService.LogWarning("[INFO] Task execution was cancelled by user.");
                // Rethrow OperationCanceledException so TaskService cleanly sets AgentTaskStatus.Cancelled
                throw new OperationCanceledException(cancellationToken);
            }
            else
            {
                _logService.LogError($"[INFO] Task execution timed out after {_timeoutMinutes} minute(s).");
                return new AgentResult
                {
                    Success = false,
                    ExitCode = -1,
                    StartedAt = startTime,
                    CompletedAt = endTime,
                    StandardOutput = stdOutBuilder.ToString(),
                    StandardError = stdErrBuilder.ToString(),
                    ErrorMessage = $"Task execution timed out after {_timeoutMinutes} minute(s)."
                };
            }
        }
        catch (Exception ex)
        {
            var endTime = DateTime.Now;
            _logService.LogError("Unexpected error running Antigravity process", ex);
            return new AgentResult
            {
                Success = false,
                ExitCode = -1,
                StartedAt = startTime,
                CompletedAt = endTime,
                StandardOutput = stdOutBuilder.ToString(),
                StandardError = stdErrBuilder.ToString(),
                ErrorMessage = ex.Message
            };
        }
    }

    private static bool IsPermissionDenied(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();
        return lower.Contains("tool required the \"command\" permission") ||
               lower.Contains("tool required the \"write_file\" permission") ||
               lower.Contains("headless mode cannot prompt") ||
               lower.Contains("permission auto-denied") ||
               lower.Contains("auto-denied") ||
               lower.Contains("add an allow-rule under permissions.allow");
    }
}
