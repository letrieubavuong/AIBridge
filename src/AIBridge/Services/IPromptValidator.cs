using System.Collections.Generic;
using AIBridge.Models;

namespace AIBridge.Services;

public class PromptValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public interface IPromptValidator
{
    PromptValidationResult Validate(ExecutionPromptPackage promptPackage, ProjectPlan? currentPlan);
}
