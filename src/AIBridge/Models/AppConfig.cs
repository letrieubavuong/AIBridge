using System.Collections.Generic;
using System.Text.Json.Serialization;
using AIBridge.Infrastructure;

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

    // AI Brain Configuration (Phase 05)
    public string BrainProvider { get; set; } = "mock";
    public string BrainModel { get; set; } = "Development-Mock-v1";
    public string BrainEndpoint { get; set; } = string.Empty;
    public int BrainTimeoutSeconds { get; set; } = 120;
    public bool BrainEnabled { get; set; } = true;

    [JsonConverter(typeof(SafeAutomationModeConverter))]
    public AutomationMode AutomationMode { get; set; } = AutomationMode.Manual;
}
