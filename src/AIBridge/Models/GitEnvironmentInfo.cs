namespace AIBridge.Models;

public class GitEnvironmentInfo
{
    public bool GitInstalled { get; set; }
    public string Version { get; set; } = string.Empty;
    public string GitPath { get; set; } = string.Empty;
    public bool IsRepository { get; set; }
    public string RepositoryRoot { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public bool RemoteConfigured { get; set; }
    public string RemoteName { get; set; } = string.Empty;
    public string RemoteUrlSafe { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
