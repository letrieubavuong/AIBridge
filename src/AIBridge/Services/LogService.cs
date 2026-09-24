using System.Collections.ObjectModel;
using System.Windows;
using AIBridge.Infrastructure;

namespace AIBridge.Services;

public class LogService : ILogService
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _allEntries = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public void LogInfo(string message)
    {
        AddLog(LogLevel.Info, message);
    }

    public void LogWarning(string message)
    {
        AddLog(LogLevel.Warning, message);
    }

    public void LogError(string message, Exception? exception = null)
    {
        var formattedMessage = exception != null 
            ? $"{message} ({exception.GetType().Name}: {exception.Message})" 
            : message;
        AddLog(LogLevel.Error, formattedMessage);
    }

    public IReadOnlyList<LogEntry> GetRecentLogs(int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        lock (_lock)
        {
            return _allEntries.TakeLast(limit).ToList();
        }
    }

    private void AddLog(LogLevel level, string message)
    {
        var entry = new LogEntry(level, message);
        lock (_lock)
        {
            _allEntries.Add(entry);
        }

        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(() => LogEntries.Add(entry));
        }
        else
        {
            LogEntries.Add(entry);
        }
    }
}
