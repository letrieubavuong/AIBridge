using System;

namespace AIBridge.Models;

public class TunnelInfo
{
    public TunnelStatus Status { get; set; } = TunnelStatus.Stopped;
    public string? PublicUrl { get; set; }
    public string? PublicMcpEndpoint { get; set; }
    public string? Version { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsPortable { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
}
