using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public class HumanApprovalService : IHumanApprovalService
{
    private readonly List<HumanApprovalRecord> _records = new();
    private readonly object _lock = new();

    public Task<HumanApprovalRecord?> GetValidApprovalAsync(
        string projectId,
        string phaseId,
        string taskId,
        int planVersion,
        string? promptId = null,
        string? promptHash = null,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var match = _records
                .Where(r => r.IsValid
                            && string.Equals(r.ProjectId, projectId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(r.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(r.TaskId, taskId, StringComparison.OrdinalIgnoreCase)
                            && r.PlanVersion == planVersion)
                .Where(r => string.IsNullOrEmpty(r.PromptHash) || string.Equals(r.PromptHash, promptHash, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.ApprovedAt)
                .FirstOrDefault();

            return Task.FromResult(match);
        }
    }

    public Task<HumanApprovalRecord> ApproveAsync(
        HumanApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var record = new HumanApprovalRecord
        {
            ApprovalId = Guid.NewGuid().ToString("N"),
            ProjectId = request.ProjectId,
            PhaseId = request.PhaseId,
            TaskId = request.TaskId,
            PlanVersion = request.PlanVersion,
            ApprovalType = "ExecutionApproval",
            ApprovedAt = DateTime.Now,
            ApprovedBy = string.IsNullOrWhiteSpace(request.ApprovedBy) ? "LocalHuman" : request.ApprovedBy,
            PromptId = request.PromptId,
            PromptHash = request.PromptHash,
            ConsumedAt = null,
            IsRevoked = false
        };

        lock (_lock)
        {
            _records.Add(record);
        }

        return Task.FromResult(record);
    }

    public Task<HumanApprovalRecord> ApproveAsync(
        string projectId,
        string phaseId,
        string taskId,
        int planVersion,
        string? promptId = null,
        string? promptHash = null,
        string approvedBy = "LocalHuman",
        CancellationToken cancellationToken = default)
    {
        return ApproveAsync(new HumanApprovalRequest
        {
            ProjectId = projectId,
            PhaseId = phaseId,
            TaskId = taskId,
            PlanVersion = planVersion,
            PromptId = promptId,
            PromptHash = promptHash,
            ApprovedBy = approvedBy
        }, cancellationToken);
    }

    public Task RevokeAsync(
        string approvalId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(approvalId)) return Task.CompletedTask;

        lock (_lock)
        {
            var record = _records.FirstOrDefault(r => string.Equals(r.ApprovalId, approvalId, StringComparison.OrdinalIgnoreCase));
            if (record != null)
            {
                record.IsRevoked = true;
            }
        }

        return Task.CompletedTask;
    }

    public Task ConsumeApprovalAsync(
        string approvalId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(approvalId)) return Task.CompletedTask;

        lock (_lock)
        {
            var record = _records.FirstOrDefault(r => string.Equals(r.ApprovalId, approvalId, StringComparison.OrdinalIgnoreCase));
            if (record != null && !record.ConsumedAt.HasValue)
            {
                record.ConsumedAt = DateTime.Now;
            }
        }

        return Task.CompletedTask;
    }
}
