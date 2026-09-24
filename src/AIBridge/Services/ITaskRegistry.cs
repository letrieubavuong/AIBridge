using System.Collections.Generic;
using AIBridge.Models;

namespace AIBridge.Services;

public interface ITaskRegistry
{
    void RegisterTask(AgentTask task);
    void RecordResult(string taskId, AgentResult result);
    void RecordGitEvidence(string taskId, GitEvidence evidence);
    AgentTask? GetTask(string taskId);
    TaskExecutionRecord? GetRecord(string taskId);
    GitEvidence? GetGitEvidence(string taskId);
    TaskExecutionRecord? GetCurrentRecord();
    IReadOnlyList<TaskExecutionRecord> GetRecentRecords(int limit = 50);
}
