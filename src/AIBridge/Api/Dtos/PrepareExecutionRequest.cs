using System.Collections.Generic;

namespace AIBridge.Api.Dtos;

public class PrepareExecutionRequest
{
    public string Instructions { get; set; } = string.Empty;
    public List<string> VerificationInstructions { get; set; } = new();
    public string CommitInstructions { get; set; } = string.Empty;
    public string GeneratedBy { get; set; } = "ChatGPTWeb";
}
