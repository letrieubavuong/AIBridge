using System;
using System.IO;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public class GitEnvironmentService : IGitEnvironmentService
{
    private readonly ILogService _logService;
    private readonly IGitCommandService _commandService;

    public GitEnvironmentService(ILogService logService, IGitCommandService commandService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
    }

    public string ResolveGitExecutable(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var trimmed = configuredPath.Trim('"').Trim();
            if (File.Exists(trimmed))
            {
                return Path.GetFullPath(trimmed);
            }
        }

        // Search PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in paths)
            {
                try
                {
                    var candidate = Path.Combine(p.Trim(), "git.exe");
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch { }
            }
        }

        // Common Windows fallbacks
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var fallbacks = new[]
        {
            Path.Combine(programFiles, "Git", "cmd", "git.exe"),
            Path.Combine(programFilesX86, "Git", "cmd", "git.exe"),
            Path.Combine(localAppData, "Programs", "Git", "cmd", "git.exe"),
            @"C:\Program Files\Git\cmd\git.exe"
        };

        foreach (var fb in fallbacks)
        {
            if (File.Exists(fb))
            {
                return Path.GetFullPath(fb);
            }
        }

        return "git"; // Default to system PATH execution
    }

    public async Task<GitEnvironmentInfo> DetectAndVerifyEnvironmentAsync(string? workspacePath = null, string? configuredGitPath = null)
    {
        var resolvedGit = ResolveGitExecutable(configuredGitPath);
        var version = await _commandService.GetVersionAsync(resolvedGit);

        if (string.IsNullOrEmpty(version))
        {
            _logService.LogWarning("Git CLI ('git.exe') not found or failed execution.");
            return new GitEnvironmentInfo
            {
                GitInstalled = false,
                GitPath = resolvedGit,
                ErrorMessage = "Git CLI ('git.exe') not found or unavailable."
            };
        }

        var info = new GitEnvironmentInfo
        {
            GitInstalled = true,
            Version = version,
            GitPath = resolvedGit
        };

        if (!string.IsNullOrWhiteSpace(workspacePath) && Directory.Exists(workspacePath))
        {
            bool isRepo = await _commandService.IsRepositoryAsync(workspacePath, resolvedGit);
            info.IsRepository = isRepo;

            if (isRepo)
            {
                var repoRoot = await _commandService.GetRepositoryRootAsync(workspacePath, resolvedGit);
                info.RepositoryRoot = repoRoot ?? workspacePath;

                var branch = await _commandService.GetCurrentBranchAsync(info.RepositoryRoot, resolvedGit);
                info.Branch = branch ?? "HEAD";

                var (remoteName, remoteUrlSafe) = await _commandService.GetRemoteUrlAsync(info.RepositoryRoot, resolvedGit);
                info.RemoteName = remoteName;
                info.RemoteUrlSafe = remoteUrlSafe;
                info.RemoteConfigured = !string.IsNullOrEmpty(remoteName);
            }
        }

        return info;
    }
}
