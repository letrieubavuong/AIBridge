using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IGitCommandService
{
    Task<(int ExitCode, string Stdout, string Stderr, TimeSpan Duration)> ExecuteGitCommandAsync(
        string? workingDirectory, 
        string gitExecutable, 
        string[] args, 
        CancellationToken cancellationToken = default, 
        TimeSpan? timeout = null);

    Task<string?> GetVersionAsync(string gitExecutable, CancellationToken cancellationToken = default);
    Task<bool> IsRepositoryAsync(string workspacePath, string gitExecutable, CancellationToken cancellationToken = default);
    Task<string?> GetRepositoryRootAsync(string workspacePath, string gitExecutable, CancellationToken cancellationToken = default);
    Task<string?> GetCurrentBranchAsync(string repoRoot, string gitExecutable, CancellationToken cancellationToken = default);
    Task<string?> GetHeadShaAsync(string repoRoot, string gitExecutable, CancellationToken cancellationToken = default);
    Task<string?> GetHeadMessageAsync(string repoRoot, string sha, string gitExecutable, CancellationToken cancellationToken = default);
    Task<(List<string> Changed, List<string> Untracked, bool IsDirty)> GetStatusAsync(string repoRoot, string gitExecutable, CancellationToken cancellationToken = default);
    Task<(string? DiffText, bool IsTruncated)> GetDiffAsync(string repoRoot, string? fromSha, string? toSha, string gitExecutable, int maxBytes = 262144, CancellationToken cancellationToken = default);
    Task<(string? DiffText, bool IsTruncated)> GetWorkingTreeDiffAsync(string repoRoot, string gitExecutable, int maxBytes = 262144, CancellationToken cancellationToken = default);
    Task<(string RemoteName, string RemoteUrlSafe)> GetRemoteUrlAsync(string repoRoot, string gitExecutable, CancellationToken cancellationToken = default);
    Task<AheadBehindResult> GetAheadBehindAsync(string repoRoot, string branch, string remoteName, string gitExecutable, CancellationToken cancellationToken = default);
    Task<List<GitCommitInfo>> GetCommitsBetweenAsync(string repoRoot, string fromSha, string toSha, string gitExecutable, CancellationToken cancellationToken = default);
    Task<List<ChangedFileInfo>> GetNameStatusDiffAsync(string repoRoot, string? fromSha, string? toSha, string gitExecutable, CancellationToken cancellationToken = default);
    Task<(bool Success, string Output, string ErrorMessage)> PushAsync(string repoRoot, string remoteName, string branch, string gitExecutable, CancellationToken cancellationToken = default);
}
