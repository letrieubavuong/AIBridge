using AIBridge.Models;

namespace AIBridge.Services;

public interface IBridgeServer : IDisposable
{
    BridgeStatus Status { get; }
    string? ErrorMessage { get; }
    string Host { get; }
    int Port { get; }
    event Action<BridgeStatus>? StatusChanged;
    Task<bool> StartAsync();
    Task StopAsync();
}
