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
}
