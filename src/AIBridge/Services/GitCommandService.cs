using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Infrastructure;
using AIBridge.Models;

namespace AIBridge.Services;

public class GitCommandService : IGitCommandService
{
    private readonly ILogService _logService;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public GitCommandService(ILogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public async Task<(int ExitCode, string Stdout, string Stderr, TimeSpan Duration)> ExecuteGitCommandAsync(
        string? workingDirectory,
        string gitExecutable,
        string[] args,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(gitExecutable))
        {
            return (-1, string.Empty, "Git executable path is empty.", TimeSpan.Zero);
        }

        var effectiveTimeout = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        var startInfo = new ProcessStartInfo
        {
            FileName = gitExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            startInfo.WorkingDirectory = Path.GetFullPath(workingDirectory);
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) stderrBuilder.AppendLine(e.Data);
        };

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
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
            stopwatch.Stop();

            return (process.ExitCode, stdoutBuilder.ToString().Trim(), stderrBuilder.ToString().Trim(), stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }

            var err = cancellationToken.IsCancellationRequested 
                ? "Git operation cancelled by user." 
                : $"Git operation timed out after {effectiveTimeout.TotalSeconds} seconds.";
            return (-1, stdoutBuilder.ToString().Trim(), err, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return (-1, string.Empty, $"Git execution exception: {ex.Message}", stopwatch.Elapsed);
        }
    }

    public async Task<string?> GetVersionAsync(string gitExecutable, CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(null, gitExecutable, new[] { "--version" }, cancellationToken);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            // e.g. "git version 2.43.0.windows.1" -> "2.43.0.windows.1"
            var parts = stdout.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 ? parts[2] : stdout;
        }
        return null;
    }

    public async Task<bool> IsRepositoryAsync(string workspacePath, string gitExecutable, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
        {
            return false;
        }

        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(workspacePath, gitExecutable, new[] { "rev-parse", "--is-inside-work-tree" }, cancellationToken);
        return exitCode == 0 && stdout.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string?> GetRepositoryRootAsync(string workspacePath, string gitExecutable, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
        {
            return null;
        }

        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(workspacePath, gitExecutable, new[] { "rev-parse", "--show-toplevel" }, cancellationToken);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            try
            {
                return Path.GetFullPath(stdout.Trim());
            }
            catch
            {
                return stdout.Trim();
            }
        }
        return null;
    }

    public async Task<string?> GetCurrentBranchAsync(string repoRoot, string gitExecutable, CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(repoRoot, gitExecutable, new[] { "rev-parse", "--abbrev-ref", "HEAD" }, cancellationToken);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            return stdout.Trim();
        }
        return null;
    }

    public async Task<string?> GetHeadShaAsync(string repoRoot, string gitExecutable, CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(repoRoot, gitExecutable, new[] { "rev-parse", "HEAD" }, cancellationToken);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout) && stdout.Length >= 7)
        {
            return stdout.Trim();
        }
        return null;
    }

    public async Task<string?> GetHeadMessageAsync(string repoRoot, string sha, string gitExecutable, CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(sha) ? "HEAD" : sha;
        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(repoRoot, gitExecutable, new[] { "log", "-1", "--format=%B", target }, cancellationToken);
        if (exitCode == 0)
        {
            return stdout.Trim();
        }
        return null;
    }

    public async Task<(List<string> Changed, List<string> Untracked, bool IsDirty)> GetStatusAsync(
        string repoRoot, string gitExecutable, CancellationToken cancellationToken = default)
    {
        var changed = new List<string>();
        var untracked = new List<string>();

        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(repoRoot, gitExecutable, new[] { "status", "--porcelain" }, cancellationToken);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Length < 4) continue;
                var statusCode = line.Substring(0, 2);
                var filePath = line.Substring(3).Trim();

                if (statusCode == "??" || statusCode == "!!")
                {
                    untracked.Add(filePath);
                }
                else
                {
                    changed.Add(filePath);
                }
            }
        }

        bool isDirty = changed.Count > 0 || untracked.Count > 0;
        return (changed, untracked, isDirty);
    }

    public async Task<string?> GetDiffAsync(
        string repoRoot, string? fromSha, string? toSha, string gitExecutable, int maxBytes = 262144, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromSha) || string.IsNullOrWhiteSpace(toSha) || fromSha == toSha)
        {
            return null;
        }

        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(repoRoot, gitExecutable, new[] { "diff", $"{fromSha}..{toSha}" }, cancellationToken);
        if (exitCode == 0)
        {
            return stdout;
        }
        return null;
    }

    public async Task<string?> GetWorkingTreeDiffAsync(
        string repoRoot, string gitExecutable, int maxBytes = 262144, CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(repoRoot, gitExecutable, new[] { "diff", "HEAD" }, cancellationToken);
        if (exitCode == 0)
        {
            return stdout;
        }
        return null;
    }

    public async Task<(string RemoteName, string RemoteUrlSafe)> GetRemoteUrlAsync(
        string repoRoot, string gitExecutable, CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(repoRoot, gitExecutable, new[] { "remote", "-v" }, cancellationToken);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var remoteName = parts[0].Trim();
                    var urlAndType = parts[1].Trim();
                    var url = urlAndType.Split(' ')[0].Trim();

                    // Prefer origin
                    if (remoteName.Equals("origin", StringComparison.OrdinalIgnoreCase) || lines.Length <= 2)
                    {
                        var safeUrl = SecretRedactor.RedactUrl(url);
                        return (remoteName, safeUrl);
                    }
                }
            }
        }
        return (string.Empty, string.Empty);
    }

    public async Task<(int Ahead, int Behind)> GetAheadBehindAsync(
        string repoRoot, string branch, string remoteName, string gitExecutable, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.Equals("HEAD", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(remoteName))
        {
            return (0, 0);
        }

        var upstream = $"{remoteName}/{branch}";
        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(
            repoRoot, gitExecutable, new[] { "rev-list", "--left-right", "--count", $"{branch}...{upstream}" }, cancellationToken);

        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            var parts = stdout.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out var ahead) && int.TryParse(parts[1], out var behind))
            {
                return (ahead, behind);
            }
        }

        return (0, 0);
    }

    public async Task<List<GitCommitInfo>> GetCommitsBetweenAsync(
        string repoRoot, string fromSha, string toSha, string gitExecutable, CancellationToken cancellationToken = default)
    {
        var list = new List<GitCommitInfo>();
        if (string.IsNullOrWhiteSpace(fromSha) || string.IsNullOrWhiteSpace(toSha) || fromSha == toSha)
        {
            return list;
        }

        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(
            repoRoot, gitExecutable, new[] { "log", $"{fromSha}..{toSha}", "--format=%H|%h|%an|%ct|%s" }, cancellationToken);

        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 5)
                {
                    var sha = parts[0];
                    var shortSha = parts[1];
                    var author = parts[2];
                    var timeStr = parts[3];
                    var msg = string.Join("|", parts.Skip(4));

                    DateTime dt = DateTime.Now;
                    if (long.TryParse(timeStr, out var unixSec))
                    {
                        dt = DateTimeOffset.FromUnixTimeSeconds(unixSec).LocalDateTime;
                    }

                    list.Add(new GitCommitInfo
                    {
                        Sha = sha,
                        ShortSha = shortSha,
                        Author = author,
                        Timestamp = dt,
                        Message = msg
                    });
                }
            }
        }

        return list;
    }

    public async Task<List<ChangedFileInfo>> GetNameStatusDiffAsync(
        string repoRoot, string? fromSha, string? toSha, string gitExecutable, CancellationToken cancellationToken = default)
    {
        var list = new List<ChangedFileInfo>();
        string[] args;

        if (!string.IsNullOrWhiteSpace(fromSha) && !string.IsNullOrWhiteSpace(toSha) && fromSha != toSha)
        {
            args = new[] { "diff", "--name-status", $"{fromSha}..{toSha}" };
        }
        else
        {
            args = new[] { "diff", "--name-status", "HEAD" };
        }

        var (exitCode, stdout, _, _) = await ExecuteGitCommandAsync(repoRoot, gitExecutable, args, cancellationToken);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var statusChar = parts[0][0];
                    var path = parts[1];
                    var status = statusChar switch
                    {
                        'A' => "Added",
                        'M' => "Modified",
                        'D' => "Deleted",
                        'R' => "Renamed",
                        'C' => "Copied",
                        _ => "Modified"
                    };

                    list.Add(new ChangedFileInfo
                    {
                        Path = path,
                        Status = status,
                        IsCommitted = !string.IsNullOrWhiteSpace(fromSha) && !string.IsNullOrWhiteSpace(toSha)
                    });
                }
            }
        }

        return list;
    }

    public async Task<(bool Success, string Output, string ErrorMessage)> PushAsync(
        string repoRoot, string remoteName, string branch, string gitExecutable, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteName) || string.IsNullOrWhiteSpace(branch))
        {
            return (false, string.Empty, "Remote name or branch is empty.");
        }

        // Higher timeout for network push operation (60s)
        var (exitCode, stdout, stderr, _) = await ExecuteGitCommandAsync(
            repoRoot, gitExecutable, new[] { "push", remoteName, branch }, cancellationToken, TimeSpan.FromSeconds(60));

        if (exitCode == 0)
        {
            return (true, stdout, string.Empty);
        }
        else
        {
            return (false, stdout, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }
    }
}
