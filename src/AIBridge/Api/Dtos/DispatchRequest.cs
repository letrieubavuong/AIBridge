namespace AIBridge.Api.Dtos;

public class DispatchRequest
{
    public string PromptId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 600;
}
