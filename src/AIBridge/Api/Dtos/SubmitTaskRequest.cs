namespace AIBridge.Api.Dtos;

public class SubmitTaskRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
}
