using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IGitEvidenceService
{
    Task<GitSnapshot?> CaptureSnapshotAsync(string workspacePath, string? configuredGitPath = null, CancellationToken cancellationToken = default);
    
    Task<GitEvidence> CalculateEvidenceAsync(
        string taskId, 
        string workspacePath, 
        GitSnapshot? beforeSnapshot, 
        GitSnapshot? afterSnapshot, 
        string? configuredGitPath = null, 
        CancellationToken cancellationToken = default);

    Task<(bool Success, string PushState, string Message)> PushTaskCommitAsync(
        string taskId, 
        string workspacePath, 
        AppConfig config, 
        CancellationToken cancellationToken = default);
}
