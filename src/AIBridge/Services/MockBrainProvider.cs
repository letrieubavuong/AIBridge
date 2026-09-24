using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public class MockBrainProvider : IAIBrainProvider
{
    public BrainProviderDescriptor Descriptor { get; } = new()
    {
        Id = "mock",
        DisplayName = "Mock Brain (Development / Test)",
        RequiresAuthentication = false,
        IsConfigured = true,
        IsAvailable = true,
        SupportsPlanning = true,
        SupportsReview = true
    };

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public async Task<BrainResponse> AnalyzeAsync(BrainRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        await Task.Delay(50, cancellationToken); // Simulate minor processing latency
        sw.Stop();

        var response = new BrainResponse
        {
            RequestId = request.RequestId,
            ProviderId = Descriptor.Id,
            Model = "Development-Mock-v1",
            DurationMs = sw.ElapsedMilliseconds,
            Confidence = 1.0
        };

        switch (request.RequestType)
        {
            case BrainRequestType.ReviewResult:
                if (request.AgentResult != null && request.AgentResult.Success)
                {
                    response.Decision = BrainDecision.PASS;
                    response.Reason = "Agent execution completed successfully with exit code 0.";
                    response.Summary = "PASS: Task output and evidence verified successfully.";
                }
                else
                {
                    response.Decision = BrainDecision.RETRY;
                    response.Reason = request.AgentResult?.ErrorMessage ?? "Agent execution failed or exited with non-zero exit code.";
                    response.Summary = "RETRY: Execution failed; retry attempt requested with corrected context.";
                    response.NextTask = new NextTaskInfo
                    {
                        Title = "Retry Failed Task",
                        Prompt = $"Fix previous error: {request.AgentResult?.ErrorMessage ?? "Unknown failure"}"
                    };
                }
                break;

            case BrainRequestType.Plan:
            case BrainRequestType.GenerateTask:
                response.Decision = BrainDecision.NEXT_TASK;
                response.Reason = "Task generation plan produced next actionable step.";
                response.Summary = "NEXT_TASK: Generated next task execution step.";
                response.NextTask = new NextTaskInfo
                {
                    Title = "Execute Planned Task",
                    Prompt = string.IsNullOrWhiteSpace(request.TaskPrompt) ? "Execute workspace task" : request.TaskPrompt
                };
                break;

            case BrainRequestType.DiagnoseFailure:
                response.Decision = BrainDecision.BLOCKED;
                response.Reason = "Diagnosed unrecoverable process failure.";
                response.Summary = "BLOCKED: Execution halted due to fatal error.";
                response.RequiresHumanApproval = true;
                response.HumanGateReason = "Fatal task failure requires human review.";
                break;

            case BrainRequestType.ContinuePhase:
                response.Decision = BrainDecision.NEXT_PHASE;
                response.Reason = "All tasks in current phase verified complete.";
                response.Summary = "NEXT_PHASE: Transition to next milestone phase allowed.";
                response.RequiresHumanApproval = true;
                response.HumanGateReason = "Phase transition requires human gate approval.";
                break;

            default:
                response.Decision = BrainDecision.PASS;
                response.Reason = "Mock Brain default analysis completed successfully.";
                response.Summary = "PASS: Analysis complete.";
                break;
        }

        return response;
    }
}
