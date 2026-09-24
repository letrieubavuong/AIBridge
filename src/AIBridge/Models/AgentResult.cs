namespace AIBridge.Models;

public class AgentResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; } = -1;
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
