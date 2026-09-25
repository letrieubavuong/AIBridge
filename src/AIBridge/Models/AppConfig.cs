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

    // Coding Agent Configuration (Phase 07)
    public string CodingAgentProvider { get; set; } = "antigravity";
    public int CodingAgentTimeoutSeconds { get; set; } = 600;
    public bool CodingAgentEnabled { get; set; } = true;

    // MCP Server Configuration (Phase 08)
    public bool McpEnabled { get; set; } = true;
    public string McpHost { get; set; } = "127.0.0.1";
    public int McpPort { get; set; } = 8788;
    public Dictionary<string, string> ProjectWorkspaces { get; set; } = new();

    // Tunnel Configuration (Phase 08 Integration)
    public string TunnelProvider { get; set; } = "cloudflare";
    public string CloudflaredPath { get; set; } = string.Empty;
    public bool TunnelAutoStart { get; set; } = false;
}
