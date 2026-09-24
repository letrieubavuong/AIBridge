using AIBridge.Models;

namespace AIBridge.Services;

public interface ITaskRegistry
{
    void RegisterTask(AgentTask task);
    void RecordResult(string taskId, AgentResult result);
    AgentTask? GetTask(string taskId);
    TaskExecutionRecord? GetRecord(string taskId);
    TaskExecutionRecord? GetCurrentRecord();
    IReadOnlyList<TaskExecutionRecord> GetRecentRecords(int limit = 50);
}
