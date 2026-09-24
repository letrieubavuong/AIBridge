namespace AIBridge.Models;

public class BrainProviderDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool RequiresAuthentication { get; set; }
    public bool IsConfigured { get; set; }
    public bool IsAvailable { get; set; }
    public bool SupportsPlanning { get; set; }
    public bool SupportsReview { get; set; }
}
