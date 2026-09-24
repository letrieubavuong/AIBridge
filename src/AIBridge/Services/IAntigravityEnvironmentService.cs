using AIBridge.Models;

namespace AIBridge.Services;

public interface IAntigravityEnvironmentService
{
    AntigravityEnvironmentInfo CurrentInfo { get; }
    event Action<AntigravityEnvironmentInfo>? EnvironmentInfoChanged;

    string ResolveCliExecutable(string? configuredPath = null);
    Task<AntigravityEnvironmentInfo> DetectAndVerifyEnvironmentAsync(string? configuredPath = null);
    Task<bool> InstallCliAsync(CancellationToken cancellationToken = default);
    Task<CliAuthState> CheckAuthenticationAsync(string? configuredPath = null);
    Task LaunchAuthenticationSetupAsync(string? configuredPath = null);
}
