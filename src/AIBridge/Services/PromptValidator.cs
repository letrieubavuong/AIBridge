using System;
using System.Linq;
using System.Text;
using AIBridge.Infrastructure;
using AIBridge.Models;

namespace AIBridge.Services;

public class PromptValidator : IPromptValidator
{
    public const int MaxExecutionPromptBytes = 262144; // 256 KB Limit

    public PromptValidationResult Validate(ExecutionPromptPackage promptPackage, ProjectPlan? currentPlan)
    {
        var result = new PromptValidationResult();

        if (promptPackage == null)
        {
            result.IsValid = false;
            result.Errors.Add("Prompt package cannot be null.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(promptPackage.PromptId))
        {
            result.Errors.Add("Prompt package must have a valid PromptId.");
        }

        if (string.IsNullOrWhiteSpace(promptPackage.ProjectId))
        {
            result.Errors.Add("Prompt package must specify a ProjectId.");
        }

        if (string.IsNullOrWhiteSpace(promptPackage.PhaseId))
        {
            result.Errors.Add("Prompt package must specify a PhaseId.");
        }

        if (string.IsNullOrWhiteSpace(promptPackage.TaskId))
        {
            result.Errors.Add("Prompt package must specify a TaskId.");
        }

        if (string.IsNullOrWhiteSpace(promptPackage.TaskTitle))
        {
            result.Errors.Add("Prompt package must contain a non-empty TaskTitle.");
        }

        if (string.IsNullOrWhiteSpace(promptPackage.TaskObjective))
        {
            result.Errors.Add("Prompt package must contain a non-empty TaskObjective.");
        }

        if (string.IsNullOrWhiteSpace(promptPackage.GeneratedPrompt))
        {
            result.Errors.Add("Prompt package GeneratedPrompt cannot be empty.");
        }
        else
        {
            int byteSize = Encoding.UTF8.GetByteCount(promptPackage.GeneratedPrompt);
            if (byteSize > MaxExecutionPromptBytes)
            {
                result.Errors.Add($"Generated prompt size ({byteSize} bytes) exceeds maximum allowable limit of {MaxExecutionPromptBytes} bytes.");
            }

            // Secret leakage check
            var (_, containsRedactions) = SecretRedactor.Redact(promptPackage.GeneratedPrompt);
            if (containsRedactions)
            {
                result.Errors.Add("Generated prompt contains unredacted credentials or sensitive tokens.");
            }
        }

        if (currentPlan != null)
        {
            if (!string.Equals(promptPackage.ProjectId, currentPlan.ProjectId, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add($"Prompt package ProjectId '{promptPackage.ProjectId}' does not match current plan ProjectId '{currentPlan.ProjectId}'.");
            }

            if (promptPackage.PlanVersion != currentPlan.Version)
            {
                result.Errors.Add($"PROMPT_STALE: Prompt plan version (v{promptPackage.PlanVersion}) does not match current project plan version (v{currentPlan.Version}).");
            }

            var targetPhase = currentPlan.Phases.FirstOrDefault(p => string.Equals(p.PhaseId, promptPackage.PhaseId, StringComparison.OrdinalIgnoreCase));
            if (targetPhase == null)
            {
                result.Errors.Add($"Phase '{promptPackage.PhaseId}' specified in prompt package does not exist in current project plan.");
            }
            else
            {
                var targetTask = targetPhase.Tasks.FirstOrDefault(t => string.Equals(t.TaskId, promptPackage.TaskId, StringComparison.OrdinalIgnoreCase));
                if (targetTask == null)
                {
                    result.Errors.Add($"Task '{promptPackage.TaskId}' specified in prompt package does not exist in phase '{targetPhase.PhaseId}'.");
                }
            }
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }
}
