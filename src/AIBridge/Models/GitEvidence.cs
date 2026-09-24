using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class GitEvidence
{
    public string TaskId { get; set; } = string.Empty;
    public bool IsRepository { get; set; }
    public string Status { get; set; } = "Ready"; // Ready, Pending, Unavailable, NOT_A_GIT_REPOSITORY, GIT_NOT_INSTALLED
    public GitSnapshot? BeforeSnapshot { get; set; }
    public GitSnapshot? AfterSnapshot { get; set; }
    public bool HeadChanged { get; set; }
    public bool NewCommitDetected { get; set; }
    public string? NewCommitSha { get; set; }
    public string? NewCommitShortSha { get; set; }
    public string? NewCommitMessage { get; set; }
    public string? CommitRange { get; set; }
    public List<GitCommitInfo> CommitList { get; set; } = new();
    public List<ChangedFileInfo> ChangedFiles { get; set; } = new();
    public List<ChangedFileInfo> CommittedFiles { get; set; } = new();
    public List<ChangedFileInfo> UncommittedFiles { get; set; } = new();
    public bool PreExistingChanges { get; set; }
    public string? Diff { get; set; }
    public bool DiffTruncated { get; set; }
    public bool ContainsRedactions { get; set; }
    public string PushState { get; set; } = "NotPushed"; // NotConfigured, Unknown, NotPushed, Pushed, Error
    public string? RemoteHeadSha { get; set; }
    public string? GitHubOwner { get; set; }
    public string? GitHubRepository { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime CapturedAt { get; set; } = DateTime.Now;
}
