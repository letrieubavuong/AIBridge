namespace AIBridge.Infrastructure;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

public class LogEntry
{
    public DateTime Timestamp { get; } = DateTime.Now;
    public LogLevel Level { get; }
    public string Message { get; }

    public LogEntry(LogLevel level, string message)
    {
        Level = level;
        Message = message ?? string.Empty;
    }

    public string FormattedText => $"{Timestamp:HH:mm:ss} [{Level.ToString().ToUpper()}] {Message}";

    public override string ToString() => FormattedText;
}
