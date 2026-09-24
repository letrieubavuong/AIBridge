namespace AIBridge.Api.Dtos;

public class DispatchRequest
{
    public string PromptId { get; set; } = string.Empty;
    public bool ConfirmHumanGate { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 600;
}
