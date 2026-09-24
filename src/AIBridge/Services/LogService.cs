using System.Collections.ObjectModel;
using System.Windows;
using AIBridge.Infrastructure;

namespace AIBridge.Services;

public class LogService : ILogService
{
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

    private void AddLog(LogLevel level, string message)
    {
        var entry = new LogEntry(level, message);
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => LogEntries.Add(entry));
        }
        else
        {
            LogEntries.Add(entry);
        }
    }
}
