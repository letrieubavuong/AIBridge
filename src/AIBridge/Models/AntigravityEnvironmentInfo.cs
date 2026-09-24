namespace AIBridge.Models;

public enum CliInstallationState
{
    Checking,
    Ready,
    NotInstalled,
    Error
}

public enum CliAuthState
{
    Unknown,
    Checking,
    Ready,
    Required,
    Error
}

public class AntigravityEnvironmentInfo
{
    public CliInstallationState InstallationState { get; set; } = CliInstallationState.Checking;
    public CliAuthState AuthState { get; set; } = CliAuthState.Unknown;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
}
