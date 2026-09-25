using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public class CloudflareTunnelService : ITunnelService
{
    private readonly IConfigService _configService;
    private readonly ILogService _logService;
    private readonly HttpClient _httpClient;

    private Process? _tunnelProcess;
    private TunnelStatus _status = TunnelStatus.Stopped;
    private string? _publicUrl;
    private string? _publicMcpEndpoint;
    private string? _version;
    private string? _errorMessage;
    private bool _isPortable;
    private string _executablePath = string.Empty;
    private DateTime? _startedAt;
    private readonly object _lock = new();

    public event EventHandler<TunnelInfo>? TunnelInfoChanged;

    public TunnelStatus Status => _status;
    public string? PublicUrl => _publicUrl;
    public string? PublicMcpEndpoint => _publicMcpEndpoint;
    public string? Version => _version;
    public string? ErrorMessage => _errorMessage;
    public bool IsInstalled => !string.IsNullOrEmpty(_executablePath) && File.Exists(_executablePath);
    public string ExecutablePath => _executablePath;

    private static readonly Regex TryCloudflareRegex = new(
        @"https://[a-zA-Z0-9-]+\.trycloudflare\.com",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public CloudflareTunnelService(IConfigService configService, ILogService logService, HttpClient? httpClient = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _httpClient = httpClient ?? new HttpClient();

        _ = CheckInstallationAsync();
    }

    public static string GetDefaultPortablePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIBridge", "tools", "cloudflared", "cloudflared.exe");
    }

    public async Task<TunnelInfo> CheckInstallationAsync()
    {
        string path = ResolveExecutablePath();
        _executablePath = path;
        _isPortable = string.Equals(path, GetDefaultPortablePath(), StringComparison.OrdinalIgnoreCase);

        if (File.Exists(path))
        {
            _version = await FetchVersionAsync(path);
            if (_status == TunnelStatus.NotInstalled)
            {
                SetStatus(TunnelStatus.Stopped);
            }
        }
        else
        {
            SetStatus(TunnelStatus.NotInstalled);
        }

        return GetInfo();
    }

    public string ResolveExecutablePath()
    {
        var cfg = _configService.LoadConfig();
        if (!string.IsNullOrWhiteSpace(cfg.CloudflaredPath) && File.Exists(cfg.CloudflaredPath))
        {
            return cfg.CloudflaredPath;
        }

        string portablePath = GetDefaultPortablePath();
        if (File.Exists(portablePath))
        {
            return portablePath;
        }

        string? systemPath = FindInSystemPath("cloudflared.exe");
        if (!string.IsNullOrEmpty(systemPath) && File.Exists(systemPath))
        {
            return systemPath;
        }

        return portablePath;
    }

    private static string? FindInSystemPath(string exeName)
    {
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var fullPath = Path.Combine(dir.Trim('"'), exeName);
                if (File.Exists(fullPath)) return fullPath;
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> FetchVersionAsync(string exePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            string stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                var match = Regex.Match(stdout, @"cloudflared\s+version\s+([0-9\.]+)");
                if (match.Success) return match.Groups[1].Value;
                return stdout.Trim();
            }
        }
        catch { }
        return null;
    }

    private static readonly string[] AllowedDownloadHosts = new[]
    {
        "github.com",
        "api.github.com",
        "github-releases.githubusercontent.com",
        "objects.githubusercontent.com",
        "cloudflare.com",
        "downloads.cloudflare.com"
    };

    public static bool IsAllowedDownloadHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        foreach (var allowed in AllowedDownloadHosts)
        {
            if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public async Task<bool> InstallCloudflaredAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        SetStatus(TunnelStatus.Installing);
        _logService.LogInfo("Starting download of Cloudflare Tunnel portable executable...");

        string targetPath = GetDefaultPortablePath();
        string targetDir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDir);

        string downloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";
        string tempFile = targetPath + ".tmp_" + Guid.NewGuid().ToString("N");

        try
        {
            Uri uri = new Uri(downloadUrl);
            if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only HTTPS download URLs are permitted.");
            }

            if (!IsAllowedDownloadHost(uri.Host))
            {
                throw new InvalidOperationException($"Download URL host '{uri.Host}' is not in the allowed domain list.");
            }

            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                var finalUri = response.RequestMessage?.RequestUri;
                if (finalUri != null)
                {
                    if (!finalUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Download redirect destination '{finalUri}' is not HTTPS.");
                    }
                    if (!IsAllowedDownloadHost(finalUri.Host))
                    {
                        throw new InvalidOperationException($"Download redirect destination host '{finalUri.Host}' is not in the allowed domain list.");
                    }
                }

                long? totalBytes = response.Content.Headers.ContentLength;
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        progress?.Report((double)totalRead / totalBytes.Value);
                    }
                }
            }

            FileInfo fi = new FileInfo(tempFile);
            if (fi.Length < 5 * 1024 * 1024)
            {
                throw new InvalidOperationException("Downloaded cloudflared binary is corrupted or incomplete (< 5MB).");
            }

            // Verify executable identity before replacing target file
            string? downloadedVersion = await FetchVersionAsync(tempFile);
            if (string.IsNullOrWhiteSpace(downloadedVersion))
            {
                throw new InvalidOperationException("Downloaded binary failed cloudflared --version identity check.");
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempFile, targetPath);
            _executablePath = targetPath;
            _isPortable = true;
            _version = downloadedVersion;

            var cfg = _configService.LoadConfig();
            cfg.CloudflaredPath = targetPath;
            _configService.SaveConfig(cfg);

            _logService.LogInfo($"Cloudflare Tunnel installed successfully (version: {_version}) to {targetPath}");
            SetStatus(TunnelStatus.Stopped);
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch { }

            _errorMessage = $"Install failed: {ex.Message}";
            _logService.LogError("Cloudflare Tunnel download error", ex);
            SetStatus(TunnelStatus.Error);
            return false;
        }
    }

    public static string BuildTunnelArguments(string targetLocalUrl)
    {
        string baseUrl = NormalizeTargetUrl(targetLocalUrl);
        return $"tunnel --url {baseUrl}";
    }

    public static string NormalizeTargetUrl(string targetLocalUrl)
    {
        if (string.IsNullOrWhiteSpace(targetLocalUrl))
        {
            throw new ArgumentException("Target local URL for tunnel cannot be null or empty. The tunnel target must originate from IMcpServer.EndpointUrl.");
        }

        if (!Uri.TryCreate(targetLocalUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Target local URL '{targetLocalUrl}' is malformed.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"Target local URL '{targetLocalUrl}' must use http or https scheme.");
        }

        bool isLoopback = uri.IsLoopback ||
                         string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

        if (!isLoopback)
        {
            throw new ArgumentException($"Target local URL '{targetLocalUrl}' must target a loopback address (127.0.0.1 or localhost).");
        }

        if (uri.Port == 9889)
        {
            throw new ArgumentException("Local Bridge port 9889 must NEVER be used as a public tunnel target.");
        }

        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    public static string? ParsePublicUrlFromOutput(string logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine)) return null;
        var match = TryCloudflareRegex.Match(logLine);
        if (match.Success)
        {
            return match.Value.TrimEnd('/');
        }
        return null;
    }

    public static string FormatPublicMcpEndpoint(string publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrl)) return string.Empty;
        string trimmed = publicBaseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        return $"{trimmed}/mcp";
    }

    public async Task<bool> StartTunnelAsync(string targetLocalUrl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_status == TunnelStatus.Connected || _status == TunnelStatus.Starting)
            {
                _logService.LogWarning("Tunnel is already running or starting.");
                return true;
            }
        }

        string path = ResolveExecutablePath();
        if (!File.Exists(path))
        {
            _errorMessage = "Cloudflare Tunnel executable (cloudflared) not found.";
            SetStatus(TunnelStatus.NotInstalled);
            return false;
        }

        SetStatus(TunnelStatus.Starting);
        _errorMessage = null;
        _publicUrl = null;
        _publicMcpEndpoint = null;

        string targetBaseUrl = NormalizeTargetUrl(targetLocalUrl);
        string args = BuildTunnelArguments(targetBaseUrl);

        _logService.LogInfo($"Starting Cloudflare Tunnel pointing to {targetBaseUrl}...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var tcsUrlFound = new TaskCompletionSource<string?>();

            proc.OutputDataReceived += (s, e) => ProcessOutputLine(e.Data, tcsUrlFound);
            proc.ErrorDataReceived += (s, e) => ProcessOutputLine(e.Data, tcsUrlFound);

            proc.Exited += (s, e) =>
            {
                lock (_lock)
                {
                    _logService.LogWarning("Cloudflare Tunnel process exited.");
                    if (_status == TunnelStatus.Connected || _status == TunnelStatus.Starting)
                    {
                        SetStatus(TunnelStatus.Stopped);
                    }
                    _tunnelProcess = null;
                }
            };

            if (!proc.Start())
            {
                _errorMessage = "Failed to start cloudflared process.";
                SetStatus(TunnelStatus.Error);
                return false;
            }

            _tunnelProcess = proc;
            _startedAt = DateTime.Now;

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Wait up to 25 seconds for public URL discovery
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                var urlTask = tcsUrlFound.Task;
                var completedTask = await Task.WhenAny(urlTask, Task.Delay(25000, linkedCts.Token));

                if (completedTask == urlTask && urlTask.IsCompletedSuccessfully && !string.IsNullOrEmpty(urlTask.Result))
                {
                    _publicUrl = urlTask.Result;
                    _publicMcpEndpoint = FormatPublicMcpEndpoint(_publicUrl);
                    _logService.LogInfo($"Cloudflare Tunnel connected! Public MCP Endpoint: {_publicMcpEndpoint}");
                    SetStatus(TunnelStatus.Connected);
                    return true;
                }
            }
            catch (OperationCanceledException) { }

            if (_status == TunnelStatus.Starting && !string.IsNullOrEmpty(_publicUrl))
            {
                SetStatus(TunnelStatus.Connected);
                return true;
            }

            _errorMessage = "Timed out waiting for Cloudflare Tunnel public URL generation.";
            _logService.LogWarning(_errorMessage);
            SetStatus(TunnelStatus.Error);
            return false;
        }
        catch (Exception ex)
        {
            _errorMessage = $"Tunnel start error: {ex.Message}";
            _logService.LogError("Cloudflare Tunnel exception", ex);
            SetStatus(TunnelStatus.Error);
            return false;
        }
    }

    private void ProcessOutputLine(string? line, TaskCompletionSource<string?> tcsUrlFound)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // Log line safely without token leakage
        _logService.LogInfo($"[Tunnel] {line}");

        string? foundUrl = ParsePublicUrlFromOutput(line);
        if (!string.IsNullOrEmpty(foundUrl))
        {
            _publicUrl = foundUrl;
            _publicMcpEndpoint = FormatPublicMcpEndpoint(foundUrl);
            tcsUrlFound.TrySetResult(foundUrl);
        }
    }

    public async Task StopTunnelAsync()
    {
        Process? proc;
        lock (_lock)
        {
            proc = _tunnelProcess;
            _tunnelProcess = null;
        }

        if (proc != null)
        {
            try
            {
                _logService.LogInfo("Stopping Cloudflare Tunnel process...");
                if (!proc.HasExited)
                {
                    try
                    {
                        if (proc.CloseMainWindow())
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                            await proc.WaitForExitAsync(cts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logService.LogWarning("Cloudflare Tunnel process did not terminate gracefully within 3s timeout. Forcing termination...");
                    }
                    catch (Exception ex)
                    {
                        _logService.LogWarning($"Graceful shutdown attempt encountered exception: {ex.Message}");
                    }

                    if (!proc.HasExited)
                    {
                        _logService.LogInfo("Force-killing Cloudflare Tunnel process tree...");
                        proc.Kill(entireProcessTree: true);
                        await proc.WaitForExitAsync();
                    }
                }
                proc.Dispose();
            }
            catch (Exception ex)
            {
                _logService.LogWarning($"Error stopping cloudflared process: {ex.Message}");
            }
        }

        _publicUrl = null;
        _publicMcpEndpoint = null;
        _startedAt = null;
        SetStatus(TunnelStatus.Stopped);
        _logService.LogInfo("Cloudflare Tunnel stopped.");
    }

    private void SetStatus(TunnelStatus status)
    {
        lock (_lock)
        {
            _status = status;
        }
        TunnelInfoChanged?.Invoke(this, GetInfo());
    }

    public TunnelInfo GetInfo()
    {
        return new TunnelInfo
        {
            Status = _status,
            PublicUrl = _publicUrl,
            PublicMcpEndpoint = _publicMcpEndpoint,
            Version = _version,
            ErrorMessage = _errorMessage,
            IsPortable = _isPortable,
            ExecutablePath = _executablePath,
            StartedAt = _startedAt
        };
    }

    public void Dispose()
    {
        StopTunnelAsync().GetAwaiter().GetResult();
    }
}
