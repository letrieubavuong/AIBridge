namespace AIBridge.Api.Dtos;

public class TaskStatusResponse
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public int? ExitCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public bool StdoutTruncated { get; set; }
    public bool StderrTruncated { get; set; }
}
