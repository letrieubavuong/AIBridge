namespace AIBridge.Api.Dtos;

public class EnvironmentResponse
{
    public AntigravityInfoResponse Antigravity { get; set; } = new();
    public bool WorkspaceConfigured { get; set; }
    public BridgeInfoResponse Bridge { get; set; } = new();
}

public class AntigravityInfoResponse
{
    public string InstallationState { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string AuthenticationState { get; set; } = string.Empty;
}

public class BridgeInfoResponse
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
}
