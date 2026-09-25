using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using AIBridge.Models;

namespace AIBridge.Services;

public class AntigravityEnvironmentService : IAntigravityEnvironmentService
{
    private readonly ILogService _logService;
    private AntigravityEnvironmentInfo _currentInfo = new();

    public AntigravityEnvironmentInfo CurrentInfo => _currentInfo;

    public event Action<AntigravityEnvironmentInfo>? EnvironmentInfoChanged;

    public AntigravityEnvironmentService(ILogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public string ResolveCliExecutable(string? configuredPath = null)
    {
        // 1. Configured path if valid file
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        // 2. User-local default path: %LOCALAPPDATA%\agy\bin\agy.exe
        var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var defaultPath = Path.Combine(localAppDataPath, "agy", "bin", "agy.exe");
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        var defaultCmd = Path.Combine(localAppDataPath, "agy", "bin", "agy.cmd");
        if (File.Exists(defaultCmd))
        {
            return defaultCmd;
        }

        // 3. Search PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pathDir in paths)
            {
                try
                {
                    var exePath = Path.Combine(pathDir, "agy.exe");
                    if (File.Exists(exePath))
                    {
                        return exePath;
                    }
                    var cmdPath = Path.Combine(pathDir, "agy.cmd");
                    if (File.Exists(cmdPath))
                    {
                        return cmdPath;
                    }
                    var batPath = Path.Combine(pathDir, "agy.bat");
                    if (File.Exists(batPath))
                    {
                        return batPath;
                    }
                }
                catch
                {
                    // Ignore invalid PATH entries
                }
            }
        }

        return string.Empty;
    }

    public async Task<AntigravityEnvironmentInfo> DetectAndVerifyEnvironmentAsync(string? configuredPath = null)
    {
        _logService.LogInfo("Detecting Antigravity CLI environment...");
        var previousAuthState = _currentInfo.AuthState;
        var info = new AntigravityEnvironmentInfo
        {
            InstallationState = CliInstallationState.Checking,
            AuthState = CliAuthState.Checking
        };
        UpdateInfo(info);

        var resolvedExecutable = ResolveCliExecutable(configuredPath);
        if (string.IsNullOrWhiteSpace(resolvedExecutable))
        {
            _logService.LogWarning("Antigravity CLI ('agy') not found in configured path, %LOCALAPPDATA%\\agy\\bin, or PATH.");
            info.InstallationState = CliInstallationState.NotInstalled;
            info.AuthState = CliAuthState.Unknown;
            info.ExecutablePath = string.Empty;
            info.Version = string.Empty;
            info.StatusMessage = "Antigravity CLI is not installed.";
            UpdateInfo(info);
            return info;
        }

        info.ExecutablePath = resolvedExecutable;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(cts.Token);

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                info.InstallationState = CliInstallationState.Ready;
                info.Version = stdout;
                info.StatusMessage = $"Antigravity CLI verified: agy {stdout} ({resolvedExecutable})";
                _logService.LogInfo(info.StatusMessage);

                // Check authentication state (reuse cached Ready state if executable path has not changed)
                if (previousAuthState == CliAuthState.Ready)
                {
                    info.AuthState = CliAuthState.Ready;
                }
                else
                {
                    info.AuthState = await CheckAuthenticationAsync(resolvedExecutable);
                }
            }
            else
            {
                info.InstallationState = CliInstallationState.Error;
                info.AuthState = CliAuthState.Unknown;
                info.StatusMessage = $"CLI verification failed with exit code {process.ExitCode}: {stderr}";
                _logService.LogWarning(info.StatusMessage);
            }
        }
        catch (Exception ex)
        {
            info.InstallationState = CliInstallationState.Error;
            info.AuthState = CliAuthState.Error;
            info.StatusMessage = $"Error verifying Antigravity CLI at '{resolvedExecutable}': {ex.Message}";
            _logService.LogError(info.StatusMessage, ex);
        }

        UpdateInfo(info);
        return info;
    }

    public async Task<bool> InstallCliAsync(CancellationToken cancellationToken = default)
    {
        _logService.LogInfo("Starting official Antigravity CLI installation via safe 2-step download & execution...");

        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"antigravity-install-{Guid.NewGuid():N}.ps1");

        try
        {
            // 1. Download official installer script via HTTPS to a temporary file
            const string installerUrl = "https://antigravity.google/install.ps1";
            _logService.LogInfo($"Downloading official installer script from '{installerUrl}'...");

            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(installerUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                var scriptBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (scriptBytes.Length == 0)
                {
                    _logService.LogError("Downloaded installer script was empty (0 bytes). Installation aborted.");
                    return false;
                }

                await File.WriteAllBytesAsync(tempScriptPath, scriptBytes, cancellationToken);
                _logService.LogInfo($"Installer script downloaded successfully to '{tempScriptPath}' ({scriptBytes.Length} bytes).");
            }

            // 2. Execute the downloaded installer script safely via PowerShell -File
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{tempScriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            _logService.LogInfo("Executing installer script via PowerShell...");

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        _logService.LogInfo($"[INSTALLER] {args.Data}");
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        _logService.LogWarning($"[INSTALLER STDERR] {args.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(cancellationToken);

                _logService.LogInfo($"Installer process exited with code {process.ExitCode}.");
            }

            // 3. Post-install check: Check %LOCALAPPDATA%\agy\bin\agy.exe directly without relying on current process PATH
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var localAgyExe = Path.Combine(localAppData, "agy", "bin", "agy.exe");

            var verifiedInfo = await DetectAndVerifyEnvironmentAsync(File.Exists(localAgyExe) ? localAgyExe : null);
            return verifiedInfo.InstallationState == CliInstallationState.Ready;
        }
        catch (Exception ex)
        {
            _logService.LogError("Antigravity CLI installation failed with an error", ex);
            return false;
        }
        finally
        {
            // Safe cleanup of temporary installer script file
            try
            {
                if (File.Exists(tempScriptPath))
                {
                    File.Delete(tempScriptPath);
                    _logService.LogInfo($"Cleaned up temporary installer file '{tempScriptPath}'.");
                }
            }
            catch (Exception ex)
            {
                _logService.LogWarning($"Failed to cleanup temporary installer file '{tempScriptPath}': {ex.Message}");
            }
        }
    }

    public async Task<CliAuthState> CheckAuthenticationAsync(string? configuredPath = null)
    {
        var executable = ResolveCliExecutable(configuredPath);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return CliAuthState.Unknown;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add("Reply with: OK");

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await process.WaitForExitAsync(cts.Token);

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();
            var combined = (stdout + "\n" + stderr).ToLowerInvariant();

            if (combined.Contains("auth required") || combined.Contains("unauthenticated") || combined.Contains("login required") || combined.Contains("please login"))
            {
                _logService.LogWarning("Antigravity CLI authentication is required.");
                return CliAuthState.Required;
            }

            if (process.ExitCode == 0 || stdout.Contains("OK"))
            {
                _logService.LogInfo("Antigravity CLI authentication verified.");
                return CliAuthState.Ready;
            }

            return CliAuthState.Required;
        }
        catch (Exception ex)
        {
            _logService.LogWarning($"Authentication check notice: {ex.Message}");
            return CliAuthState.Unknown;
        }
    }

    public Task LaunchAuthenticationSetupAsync(string? configuredPath = null)
    {
        var executable = ResolveCliExecutable(configuredPath);
        if (string.IsNullOrWhiteSpace(executable))
        {
            _logService.LogWarning("Cannot launch authentication: CLI executable not found.");
            return Task.CompletedTask;
        }

        try
        {
            _logService.LogInfo($"Launching authentication setup for '{executable}'...");
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "login",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _logService.LogError("Error launching authentication setup", ex);
        }

        return Task.CompletedTask;
    }

    private void UpdateInfo(AntigravityEnvironmentInfo info)
    {
        _currentInfo = info;
        EnvironmentInfoChanged?.Invoke(_currentInfo);
    }
}
