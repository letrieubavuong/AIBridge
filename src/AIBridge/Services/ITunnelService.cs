using System;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface ITunnelService : IDisposable
{
    TunnelStatus Status { get; }
    string? PublicUrl { get; }
    string? PublicMcpEndpoint { get; }
    string? Version { get; }
    string? ErrorMessage { get; }
    bool IsInstalled { get; }
    string ExecutablePath { get; }

    event EventHandler<TunnelInfo>? TunnelInfoChanged;

    Task<TunnelInfo> CheckInstallationAsync();
    Task<bool> InstallCloudflaredAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> StartTunnelAsync(string targetLocalUrl, CancellationToken cancellationToken = default);
    Task StopTunnelAsync();
}
