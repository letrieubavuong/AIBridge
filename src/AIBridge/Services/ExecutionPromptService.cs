using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Infrastructure;
using AIBridge.Models;

namespace AIBridge.Services;

public class ExecutionPromptService : IExecutionPromptService
{
    private readonly IPromptValidator _promptValidator;
    private readonly IGitEvidenceService? _gitEvidenceService;
    private readonly IConfigService? _configService;
    private readonly ILogService? _logService;

    private readonly List<ExecutionPromptPackage> _promptHistory = new();
    private readonly object _lock = new();

    public const int MaxPromptBytes = 262144; // 256 KB Limit

    public ExecutionPromptService(
        IPromptValidator promptValidator,
        IGitEvidenceService? gitEvidenceService = null,
        IConfigService? configService = null,
        ILogService? logService = null)
    {
        _promptValidator = promptValidator ?? throw new ArgumentNullException(nameof(promptValidator));
        _gitEvidenceService = gitEvidenceService;
        _configService = configService;
        _logService = logService;
    }

    public async Task<ExecutionPromptPackage> PreparePromptPackageAsync(
        ProjectPlan plan,
        string phaseId,
        string taskId,
        string externalInstructions = "",
        List<string>? verificationInstructions = null,
        string commitInstructions = "",
        string workspacePath = "",
        string generatedBy = "ChatGPTWeb",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(phaseId)) throw new ArgumentException("PhaseId cannot be empty.", nameof(phaseId));
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("TaskId cannot be empty.", nameof(taskId));

        var phase = plan.Phases.FirstOrDefault(p => string.Equals(p.PhaseId, phaseId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Phase '{phaseId}' not found in plan '{plan.ProjectId}'.");

        var task = phase.Tasks.FirstOrDefault(t => string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Task '{taskId}' not found in phase '{phaseId}'.");

        // 1. Authoritative Plan Criteria (Stored plan criteria cannot be overwritten/weakened by external caller)
        var authoritativeCriteria = task.AcceptanceCriteria != null && task.AcceptanceCriteria.Count > 0
            ? new List<string>(task.AcceptanceCriteria)
            : (phase.AcceptanceCriteria != null && phase.AcceptanceCriteria.Count > 0
                ? new List<string>(phase.AcceptanceCriteria)
                : new List<string> { "Implement task objective cleanly." });

        var constraints = task.Constraints != null && task.Constraints.Count > 0
            ? new List<string>(task.Constraints)
            : new List<string> { "Operate within selected workspace.", "Do not introduce breaking API changes." };

        var verificationList = verificationInstructions != null && verificationInstructions.Count > 0
            ? new List<string>(verificationInstructions)
            : new List<string> { "Verify solution restores and builds without error.", "Run unit tests if applicable." };

        string commitPolicy = !string.IsNullOrWhiteSpace(commitInstructions)
            ? commitInstructions
            : "Commit changes locally with descriptive message upon completion. Do not push to remote unless explicitly requested.";

        // 2. Capture Repository Context
        string repoContext = "Standard Workspace";
        if (_gitEvidenceService != null && !string.IsNullOrWhiteSpace(workspacePath) && Directory.Exists(workspacePath))
        {
            try
            {
                var config = _configService?.LoadConfig();
                var snapshot = await _gitEvidenceService.CaptureSnapshotAsync(workspacePath, config?.GitPath, cancellationToken);
                if (snapshot.IsGitRepository)
                {
                    repoContext = $"Git Repository | Branch: {snapshot.Branch} | Head: {snapshot.HeadShortSha} | Dirty: {snapshot.IsDirty}";
                }
            }
            catch (Exception ex)
            {
                _logService?.LogWarning($"Failed to capture repo context for prompt: {ex.Message}");
            }
        }

        // 3. Build Canonical Prompt Text with AIBridge Safety Rules & Instruction Boundaries
        var safetyRules =
            "AIBRIDGE SYSTEM EXECUTION SAFETY RULES:\n" +
            "- Work only inside the selected workspace directory.\n" +
            "- Do not modify files outside the workspace.\n" +
            "- Do not expose credentials, tokens, or secrets in code or output.\n" +
            "- Do not perform destructive Git operations (no force push, no hard reset).\n" +
            "- Do not modify global Git configuration.\n" +
            "- Do not start another phase or execute unplanned tasks.\n" +
            "- Implement only the assigned task.\n" +
            "- Inspect existing code before modifying it.\n" +
            "- Preserve existing architecture unless task explicitly requires change.";

        var sb = new StringBuilder();
        sb.AppendLine($"PROJECT: {plan.Name}");
        sb.AppendLine($"PROJECT GOAL: {plan.Goal}");
        sb.AppendLine();
        sb.AppendLine($"CURRENT PHASE: Phase {phase.PhaseNumber:D2} — {phase.Name}");
        sb.AppendLine($"PHASE OBJECTIVE: {phase.Objective}");
        sb.AppendLine();
        sb.AppendLine($"CURRENT TASK: Task {task.TaskId} — {task.Title}");
        sb.AppendLine($"TASK OBJECTIVE: {task.Objective}");
        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            sb.AppendLine($"TASK DESCRIPTION: {task.Description}");
        }
        sb.AppendLine();

        sb.AppendLine("AUTHORITATIVE ACCEPTANCE CRITERIA:");
        foreach (var c in authoritativeCriteria)
        {
            sb.AppendLine($" - {c}");
        }
        sb.AppendLine();

        if (constraints.Count > 0)
        {
            sb.AppendLine("CONSTRAINTS:");
            foreach (var con in constraints)
            {
                sb.AppendLine($" - {con}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(externalInstructions))
        {
            sb.AppendLine("EXTERNAL BRAIN CODING INSTRUCTIONS:");
            sb.AppendLine(externalInstructions.Trim());
            sb.AppendLine();
        }

        sb.AppendLine($"WORKSPACE: {workspacePath}");
        sb.AppendLine($"REPOSITORY CONTEXT: {repoContext}");
        sb.AppendLine();
        sb.AppendLine(safetyRules);
        sb.AppendLine();

        sb.AppendLine("VERIFICATION INSTRUCTIONS:");
        foreach (var v in verificationList)
        {
            sb.AppendLine($" - {v}");
        }
        sb.AppendLine();

        sb.AppendLine($"COMMIT INSTRUCTIONS:\n{commitPolicy}");

        // 4. Secret Redaction
        var (redactedPromptText, _) = SecretRedactor.Redact(sb.ToString());
        var (redactedExternalInst, _) = SecretRedactor.Redact(externalInstructions);

        var package = new ExecutionPromptPackage
        {
            GeneratedBy = string.IsNullOrWhiteSpace(generatedBy) ? "ChatGPTWeb" : generatedBy,
            ProjectId = plan.ProjectId,
            ProjectName = plan.Name,
            PhaseId = phase.PhaseId,
            PhaseNumber = phase.PhaseNumber,
            PhaseName = phase.Name,
            PhaseObjective = phase.Objective,
            TaskId = task.TaskId,
            TaskNumber = task.TaskNumber,
            TaskTitle = task.Title,
            TaskObjective = task.Objective,
            Instructions = redactedExternalInst,
            AcceptanceCriteria = authoritativeCriteria,
            Constraints = constraints,
            VerificationInstructions = verificationList,
            CommitInstructions = commitPolicy,
            WorkspaceContext = workspacePath,
            RepositoryContext = repoContext,
            GeneratedPrompt = redactedPromptText,
            GeneratedAt = DateTime.Now,
            PlanVersion = plan.Version
        };

        // 5. UTF-8 Safe Truncation (256 KB Limit)
        var (boundedPrompt, wasTruncated) = Utf8SafeTruncateWithPriority(package.GeneratedPrompt);
        package.GeneratedPrompt = boundedPrompt;
        package.WasTruncated = wasTruncated;
        package.ByteSize = Encoding.UTF8.GetByteCount(package.GeneratedPrompt);

        // 6. Compute SHA-256 Prompt Hash Fingerprint
        package.PromptHash = ComputeSha256(package.GeneratedPrompt);

        // 7. Validate Prompt Package
        var valResult = _promptValidator.Validate(package, plan);
        if (!valResult.IsValid)
        {
            throw new InvalidOperationException($"Prompt package validation failed: {string.Join("; ", valResult.Errors)}");
        }

        // 8. Store in history (max 100)
        lock (_lock)
        {
            _promptHistory.Add(package);
            if (_promptHistory.Count > 100)
            {
                _promptHistory.RemoveAt(0);
            }
        }

        return package;
    }

    public ExecutionPromptPackage? GetPromptPackage(string promptId)
    {
        lock (_lock)
        {
            return _promptHistory.FirstOrDefault(p => string.Equals(p.PromptId, promptId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<ExecutionPromptPackage> GetPromptHistory()
    {
        lock (_lock)
        {
            return _promptHistory.ToList();
        }
    }

    private static (string Text, bool WasTruncated) Utf8SafeTruncateWithPriority(string fullPrompt)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(fullPrompt);

        if (bytes.Length <= MaxPromptBytes)
        {
            return (fullPrompt, false);
        }

        int end = MaxPromptBytes - 100;
        while (end > 0 && (bytes[end] & 0xC0) == 0x80)
        {
            end--;
        }

        string truncatedText = Encoding.UTF8.GetString(bytes, 0, end) + "\n\n... [PROMPT CONTEXT TRUNCATED SAFE BOUNDARY]";
        return (truncatedText, true);
    }

    private static string ComputeSha256(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
