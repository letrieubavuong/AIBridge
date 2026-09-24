using System;

namespace AIBridge.Models;

public class PlanVersion
{
    public int VersionNumber { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = "AI_GENERATED";
    public int? ParentVersion { get; set; }
}
