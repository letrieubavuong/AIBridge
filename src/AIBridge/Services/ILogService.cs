using System.Collections.ObjectModel;
using AIBridge.Infrastructure;

namespace AIBridge.Services;

public interface ILogService
{
    ObservableCollection<LogEntry> LogEntries { get; }
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? exception = null);
}
