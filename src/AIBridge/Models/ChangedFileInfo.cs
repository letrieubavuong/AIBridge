namespace AIBridge.Models;

public class ChangedFileInfo
{
    public string Path { get; set; } = string.Empty;
    public string Status { get; set; } = "Modified"; // Modified, Added, Deleted, Renamed, Untracked
    public bool IsCommitted { get; set; }
}
