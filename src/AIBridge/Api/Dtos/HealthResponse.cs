namespace AIBridge.Api.Dtos;

public class HealthResponse
{
    public string Status { get; set; } = "ok";
    public string Bridge { get; set; } = "running";
    public string Version { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
}
