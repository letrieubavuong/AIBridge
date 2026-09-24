using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Infrastructure;
using AIBridge.Models;

namespace AIBridge.Services;

public class GitEvidenceService : IGitEvidenceService
{
    private readonly ILogService _logService;
    private readonly IGitEnvironmentService _envService;
    private readonly IGitCommandService _cmdService;
    private const int MaxDiffBytes = 262144; // 256 KB limit

    public GitEvidenceService(
        ILogService logService,
        IGitEnvironmentService envService,
        IGitCommandService cmdService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _envService = envService ?? throw new ArgumentNullException(nameof(envService));
        _cmdService = cmdService ?? throw new ArgumentNullException(nameof(cmdService));
    }

    public async Task<GitSnapshot?> CaptureSnapshotAsync(
        string workspacePath, string? configuredGitPath = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
        {
            return null;
        }

        var normalizedPath = Path.GetFullPath(workspacePath);
        var envInfo = await _envService.DetectAndVerifyEnvironmentAsync(normalizedPath, configuredGitPath);
        if (!envInfo.GitInstalled || !envInfo.IsRepository)
        {
            return new GitSnapshot
            {
                IsGitRepository = false,
                CapturedAt = DateTime.Now
            };
        }

        var gitExe = envInfo.GitPath;
        var repoRoot = envInfo.RepositoryRoot;
        var branch = envInfo.Branch;

        var headSha = await _cmdService.GetHeadShaAsync(repoRoot, gitExe, cancellationToken) ?? string.Empty;
        var headShort = headSha.Length >= 7 ? headSha[..7] : headSha;
        var headMsg = await _cmdService.GetHeadMessageAsync(repoRoot, headSha, gitExe, cancellationToken) ?? string.Empty;

        var (changed, untracked, isDirty) = await _cmdService.GetStatusAsync(repoRoot, gitExe, cancellationToken);
        var (remoteName, remoteUrlSafe) = (envInfo.RemoteName, envInfo.RemoteUrlSafe);

        var aheadBehind = await _cmdService.GetAheadBehindAsync(repoRoot, branch, remoteName, gitExe, cancellationToken);

        return new GitSnapshot
        {
            IsGitRepository = true,
            RepositoryRoot = repoRoot,
            Branch = branch,
            HeadSha = headSha,
            HeadShortSha = headShort,
            HeadMessage = headMsg,
            RemoteName = remoteName,
            RemoteUrlSafe = remoteUrlSafe,
            IsDirty = isDirty,
            ChangedFiles = changed,
            UntrackedFiles = untracked,
            AheadBehindVerified = aheadBehind.IsVerified,
            AheadCount = aheadBehind.Ahead,
            BehindCount = aheadBehind.Behind,
            CapturedAt = DateTime.Now
        };
    }

    public async Task<GitEvidence> CalculateEvidenceAsync(
        string taskId,
        string workspacePath,
        GitSnapshot? beforeSnapshot,
        GitSnapshot? afterSnapshot,
        string? configuredGitPath = null,
        CancellationToken cancellationToken = default)
    {
        var evidence = new GitEvidence
        {
            TaskId = taskId,
            BeforeSnapshot = beforeSnapshot,
            AfterSnapshot = afterSnapshot,
            CapturedAt = DateTime.Now
        };

        var envInfo = await _envService.DetectAndVerifyEnvironmentAsync(workspacePath, configuredGitPath);
        if (!envInfo.GitInstalled)
        {
            evidence.Status = "GIT_NOT_INSTALLED";
            evidence.IsRepository = false;
            evidence.Warnings.Add("Git CLI is not installed or not available in PATH.");
            return evidence;
        }

        if (!envInfo.IsRepository || beforeSnapshot == null || !beforeSnapshot.IsGitRepository)
        {
            evidence.Status = "NOT_A_GIT_REPOSITORY";
            evidence.IsRepository = false;
            evidence.Warnings.Add($"Workspace directory '{workspacePath}' is not a Git repository.");
            return evidence;
        }

        evidence.IsRepository = true;
        evidence.Status = "Ready";
        evidence.PreExistingChanges = beforeSnapshot.IsDirty;

        var gitExe = envInfo.GitPath;
        var repoRoot = envInfo.RepositoryRoot;

        // Fallback afterSnapshot if null
        afterSnapshot ??= await CaptureSnapshotAsync(workspacePath, configuredGitPath, cancellationToken);
        evidence.AfterSnapshot = afterSnapshot;

        if (afterSnapshot != null && afterSnapshot.IsGitRepository)
        {
            // GitHub Owner & Repo parsing
            ParseGitHubOwnerAndRepo(afterSnapshot.RemoteUrlSafe, evidence);

            // HEAD changed check
            evidence.HeadChanged = (!string.IsNullOrEmpty(beforeSnapshot.HeadSha) &&
                                    !string.IsNullOrEmpty(afterSnapshot.HeadSha) &&
                                    !beforeSnapshot.HeadSha.Equals(afterSnapshot.HeadSha, StringComparison.OrdinalIgnoreCase));

            evidence.NewCommitDetected = evidence.HeadChanged;

            if (evidence.NewCommitDetected)
            {
                evidence.NewCommitSha = afterSnapshot.HeadSha;
                evidence.NewCommitShortSha = afterSnapshot.HeadShortSha;
                evidence.NewCommitMessage = afterSnapshot.HeadMessage;
                evidence.CommitRange = $"{beforeSnapshot.HeadSha}..{afterSnapshot.HeadSha}";

                evidence.CommitList = await _cmdService.GetCommitsBetweenAsync(
                    repoRoot, beforeSnapshot.HeadSha, afterSnapshot.HeadSha, gitExe, cancellationToken);

                var committedFiles = await _cmdService.GetNameStatusDiffAsync(
                    repoRoot, beforeSnapshot.HeadSha, afterSnapshot.HeadSha, gitExe, cancellationToken);
                
                evidence.CommittedFiles = committedFiles;
            }

            // Uncommitted changes currently remaining in working tree
            var uncommittedFiles = new List<ChangedFileInfo>();
            foreach (var f in afterSnapshot.ChangedFiles)
            {
                uncommittedFiles.Add(new ChangedFileInfo { Path = f, Status = "Modified", IsCommitted = false });
            }
            foreach (var f in afterSnapshot.UntrackedFiles)
            {
                uncommittedFiles.Add(new ChangedFileInfo { Path = f, Status = "Untracked", IsCommitted = false });
            }
            evidence.UncommittedFiles = uncommittedFiles;

            // Combine all changed files
            var allChanged = new List<ChangedFileInfo>();
            allChanged.AddRange(evidence.CommittedFiles);
            allChanged.AddRange(evidence.UncommittedFiles);
            evidence.ChangedFiles = allChanged;

            // Diff calculation & bounding
            string? rawDiff = null;
            bool diffTruncated = false;

            if (evidence.NewCommitDetected)
            {
                var (diffResult, truncated) = await _cmdService.GetDiffAsync(repoRoot, beforeSnapshot.HeadSha, afterSnapshot.HeadSha, gitExe, MaxDiffBytes, cancellationToken);
                rawDiff = diffResult;
                diffTruncated = truncated;
            }

            // If no commit diff or additional uncommitted diff exists
            if (string.IsNullOrWhiteSpace(rawDiff) && afterSnapshot.IsDirty)
            {
                var (diffResult, truncated) = await _cmdService.GetWorkingTreeDiffAsync(repoRoot, gitExe, MaxDiffBytes, cancellationToken);
                rawDiff = diffResult;
                diffTruncated |= truncated;
            }

            if (!string.IsNullOrEmpty(rawDiff))
            {
                if (diffTruncated && !rawDiff.EndsWith("[DIFF TRUNCATED - EXCEEDED 256 KB LIMIT]"))
                {
                    rawDiff += "\n\n... [DIFF TRUNCATED - EXCEEDED 256 KB LIMIT]";
                }

                var (redactedDiff, containsRedactions) = SecretRedactor.Redact(rawDiff);
                evidence.Diff = redactedDiff;
                evidence.DiffTruncated = diffTruncated;
                evidence.ContainsRedactions = containsRedactions;
            }

            // Determine Push State
            if (string.IsNullOrEmpty(afterSnapshot.RemoteName))
            {
                evidence.PushState = "NotConfigured";
            }
            else if (!afterSnapshot.AheadBehindVerified)
            {
                evidence.PushState = "Unknown";
            }
            else if (afterSnapshot.AheadCount > 0)
            {
                evidence.PushState = "NotPushed";
            }
            else
            {
                evidence.PushState = "Pushed";
            }
        }

        return evidence;
    }

    public async Task<(bool Success, string PushState, string Message)> PushTaskCommitAsync(
        string taskId, string workspacePath, AppConfig config, CancellationToken cancellationToken = default)
    {
        var envInfo = await _envService.DetectAndVerifyEnvironmentAsync(workspacePath, config.GitPath);
        if (!envInfo.GitInstalled)
        {
            return (false, "GIT_NOT_AVAILABLE", "Git is not installed or available.");
        }

        if (!envInfo.IsRepository)
        {
            return (false, "NOT_A_GIT_REPOSITORY", "Workspace is not a Git repository.");
        }

        var gitExe = envInfo.GitPath;
        var repoRoot = envInfo.RepositoryRoot;
        var branch = envInfo.Branch;

        if (string.IsNullOrEmpty(envInfo.RemoteName))
        {
            return (false, "NO_REMOTE", "No Git remote is configured.");
        }

        if (string.Equals(branch, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "DETACHED_HEAD", "Repository is in detached HEAD state.");
        }

        // Protected branch safety check
        var protectedBranches = config.ProtectedBranches ?? new List<string> { "main", "master" };
        bool isProtected = protectedBranches.Any(pb => pb.Equals(branch, StringComparison.OrdinalIgnoreCase));

        if (isProtected && !config.AllowPushToProtectedBranches)
        {
            _logService.LogWarning($"Push blocked: Branch '{branch}' is protected and AllowPushToProtectedBranches is false.");
            return (false, "PUSH_REQUIRES_APPROVAL", $"Pushing to protected branch '{branch}' requires explicit configuration approval.");
        }

        // Check if there are commits to push
        var aheadBehind = await _cmdService.GetAheadBehindAsync(repoRoot, branch, envInfo.RemoteName, gitExe, cancellationToken);
        if (!aheadBehind.IsVerified || aheadBehind.Ahead == 0)
        {
            return (false, "NO_COMMIT_TO_PUSH", "Local branch has no unpushed commits or remote reference could not be resolved.");
        }

        _logService.LogInfo($"Executing safe git push {envInfo.RemoteName} {branch}...");
        var (success, output, errorMsg) = await _cmdService.PushAsync(repoRoot, envInfo.RemoteName, branch, gitExe, cancellationToken);

        if (success)
        {
            _logService.LogInfo($"Git push succeeded for branch '{branch}'.");
            return (true, "Pushed", "Git push succeeded.");
        }
        else
        {
            _logService.LogError($"Git push failed: {errorMsg}");
            var isAuthErr = errorMsg.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ||
                            errorMsg.Contains("Permission", StringComparison.OrdinalIgnoreCase) ||
                            errorMsg.Contains("denied", StringComparison.OrdinalIgnoreCase);

            var code = isAuthErr ? "GIT_AUTH_REQUIRED" : "PUSH_FAILED";
            return (false, code, $"Git push failed: {errorMsg}");
        }
    }

    private static void ParseGitHubOwnerAndRepo(string remoteUrl, GitEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) return;

        // https://github.com/owner/repo.git or git@github.com:owner/repo.git
        var match = Regex.Match(remoteUrl, @"github\.com[:/]([^/]+)/([^/\.]+)(\.git)?", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            evidence.GitHubOwner = match.Groups[1].Value;
            evidence.GitHubRepository = match.Groups[2].Value;
        }
    }
}
