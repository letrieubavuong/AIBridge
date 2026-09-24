namespace AIBridge.Models;

public record AheadBehindResult(
    bool IsVerified,
    int Ahead,
    int Behind,
    string? ErrorMessage = null);
