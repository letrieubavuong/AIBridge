using System;
using System.Collections.Generic;

namespace AIBridge.Models;

public class GitSnapshot
{
    public bool IsGitRepository { get; set; }
    public string RepositoryRoot { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string HeadSha { get; set; } = string.Empty;
    public string HeadShortSha { get; set; } = string.Empty;
    public string HeadMessage { get; set; } = string.Empty;
    public string RemoteName { get; set; } = string.Empty;
    public string RemoteUrlSafe { get; set; } = string.Empty;
    public bool IsDirty { get; set; }
    public List<string> ChangedFiles { get; set; } = new();
    public List<string> UntrackedFiles { get; set; } = new();
    public bool AheadBehindVerified { get; set; }
    public int AheadCount { get; set; }
    public int BehindCount { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;
}
