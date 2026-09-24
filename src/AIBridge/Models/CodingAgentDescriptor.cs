namespace AIBridge.Models;

public class CodingAgentDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool IsConfigured { get; set; }
    public bool SupportsCancellation { get; set; } = true;
    public bool SupportsWorkspace { get; set; } = true;
    public bool SupportsStreamingOutput { get; set; }
    public bool SupportsStructuredPrompt { get; set; } = true;
    public bool RequiresAuthentication { get; set; }
    public string Version { get; set; } = "1.0.0";
}
