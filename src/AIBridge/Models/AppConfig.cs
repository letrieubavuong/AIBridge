using System.Collections.Generic;

namespace AIBridge.Models;

public class AppConfig
{
    public string AntigravityPath { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public bool BridgeEnabled { get; set; } = true;
    public string BridgeHost { get; set; } = "127.0.0.1";
    public int BridgePort { get; set; } = 8787;
    public string ApiToken { get; set; } = string.Empty;
    public int AntigravityTimeoutMinutes { get; set; } = 30;

    // Git Configuration (Phase 04)
    public string GitPath { get; set; } = string.Empty;
    public bool GitAutoPush { get; set; } = false;
    public bool AllowPushToProtectedBranches { get; set; } = false;
    public List<string> ProtectedBranches { get; set; } = new() { "main", "master" };
}
