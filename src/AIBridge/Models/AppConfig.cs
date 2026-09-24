namespace AIBridge.Models;

public class AppConfig
{
    public string AntigravityPath { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public int BridgePort { get; set; } = 8787;
    public bool AutoStartBridge { get; set; } = false;
}
