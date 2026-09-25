using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IHumanApprovalService
{
    Task<HumanApprovalRecord?> GetValidApprovalAsync(
        string projectId,
        string phaseId,
        string taskId,
        int planVersion,
        string? promptId = null,
        string? promptHash = null,
        CancellationToken cancellationToken = default);

    Task<HumanApprovalRecord> ApproveAsync(
        HumanApprovalRequest request,
        CancellationToken cancellationToken = default);

    Task<HumanApprovalRecord> ApproveAsync(
        string projectId,
        string phaseId,
        string taskId,
        int planVersion,
        string? promptId = null,
        string? promptHash = null,
        string approvedBy = "LocalHuman",
        CancellationToken cancellationToken = default);

    Task RevokeAsync(
        string approvalId,
        CancellationToken cancellationToken = default);

    Task ConsumeApprovalAsync(
        string approvalId,
        CancellationToken cancellationToken = default);
}
