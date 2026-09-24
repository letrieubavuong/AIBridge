using System;

namespace AIBridge.Models;

public class BrainDecisionRecord
{
    public string RequestId { get; set; } = string.Empty;
    public BrainRequestType RequestType { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public long DurationMs { get; set; }
    public double DurationSeconds => (double)DurationMs / 1000.0;
    public BrainDecision Decision { get; set; }
    public string Summary { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
}
