namespace AIBridge.Models;

public class GitRepositoryInfo
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
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public string PushState { get; set; } = "Unknown";
}
