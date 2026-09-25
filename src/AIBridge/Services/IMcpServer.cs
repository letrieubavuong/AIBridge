using System;
using System.Threading.Tasks;

namespace AIBridge.Services;

public interface IMcpServer
{
    bool IsRunning { get; }
    string BindAddress { get; }
    int Port { get; }
    string EndpointUrl { get; }

    event EventHandler<string>? LogMessage;
    event EventHandler<bool>? StatusChanged;

    Task<bool> StartAsync();
    Task StopAsync();
}
