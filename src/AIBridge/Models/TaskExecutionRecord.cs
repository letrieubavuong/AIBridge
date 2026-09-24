namespace AIBridge.Models;

public class TaskExecutionRecord
{
    public required AgentTask Task { get; set; }
    public AgentResult? Result { get; set; }
}
