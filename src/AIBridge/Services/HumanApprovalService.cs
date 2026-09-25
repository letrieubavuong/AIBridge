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
