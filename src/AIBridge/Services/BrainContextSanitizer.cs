using System;
using System.Text;
using AIBridge.Infrastructure;
using AIBridge.Models;

namespace AIBridge.Services;

public static class BrainContextSanitizer
{
    public const int MaxContextBytes = 524288; // 512 KB

    public static BrainRequest Sanitize(BrainRequest original)
    {
        if (original == null) return new BrainRequest();

        var (sanitizedPrompt, _) = SecretRedactor.Redact(original.TaskPrompt ?? string.Empty);
        var (sanitizedProj, _) = SecretRedactor.Redact(original.ProjectContext ?? string.Empty);
        var (sanitizedPhase, _) = SecretRedactor.Redact(original.PhaseContext ?? string.Empty);
        var (sanitizedTaskCtx, _) = SecretRedactor.Redact(original.TaskContext ?? string.Empty);

        AgentResult? sanitizedAgentResult = null;
        if (original.AgentResult != null)
        {
            var (sanitizedOut, _) = SecretRedactor.Redact(original.AgentResult.StandardOutput ?? string.Empty);
            var (sanitizedErr, _) = SecretRedactor.Redact(original.AgentResult.StandardError ?? string.Empty);
            var (sanitizedErrMsg, _) = SecretRedactor.Redact(original.AgentResult.ErrorMessage ?? string.Empty);

            sanitizedAgentResult = new AgentResult
            {
                Success = original.AgentResult.Success,
                ExitCode = original.AgentResult.ExitCode,
                StandardOutput = TruncateByBytes(sanitizedOut, 131072), // 128 KB limit for stdout
                StandardError = TruncateByBytes(sanitizedErr, 65536),  // 64 KB limit for stderr
                ErrorMessage = sanitizedErrMsg,
                StartedAt = original.AgentResult.StartedAt,
                CompletedAt = original.AgentResult.CompletedAt
            };
        }

        GitEvidence? sanitizedGitEvidence = null;
        if (original.GitEvidence != null)
        {
            var (sanitizedDiff, _) = SecretRedactor.Redact(original.GitEvidence.Diff ?? string.Empty);
            sanitizedGitEvidence = new GitEvidence
            {
                TaskId = original.GitEvidence.TaskId,
                IsRepository = original.GitEvidence.IsRepository,
                Status = original.GitEvidence.Status,
                BeforeSnapshot = original.GitEvidence.BeforeSnapshot,
                AfterSnapshot = original.GitEvidence.AfterSnapshot,
                HeadChanged = original.GitEvidence.HeadChanged,
                NewCommitDetected = original.GitEvidence.NewCommitDetected,
                NewCommitSha = original.GitEvidence.NewCommitSha,
                NewCommitShortSha = original.GitEvidence.NewCommitShortSha,
                NewCommitMessage = original.GitEvidence.NewCommitMessage,
                CommitRange = original.GitEvidence.CommitRange,
                CommitList = original.GitEvidence.CommitList,
                ChangedFiles = original.GitEvidence.ChangedFiles,
                CommittedFiles = original.GitEvidence.CommittedFiles,
                UncommittedFiles = original.GitEvidence.UncommittedFiles,
                PreExistingChanges = original.GitEvidence.PreExistingChanges,
                Diff = TruncateByBytes(sanitizedDiff, 262144), // Capped at 256 KB
                DiffTruncated = original.GitEvidence.DiffTruncated,
                ContainsRedactions = original.GitEvidence.ContainsRedactions,
                PushState = original.GitEvidence.PushState,
                RemoteHeadSha = original.GitEvidence.RemoteHeadSha,
                GitHubOwner = original.GitEvidence.GitHubOwner,
                GitHubRepository = original.GitEvidence.GitHubRepository,
                Warnings = original.GitEvidence.Warnings,
                Errors = original.GitEvidence.Errors,
                CapturedAt = original.GitEvidence.CapturedAt
            };
        }

        return new BrainRequest
        {
            RequestId = original.RequestId,
            ProjectId = original.ProjectId,
            PhaseId = original.PhaseId,
            TaskId = original.TaskId,
            RequestType = original.RequestType,
            ProjectContext = TruncateByBytes(sanitizedProj, 65536),
            PhaseContext = TruncateByBytes(sanitizedPhase, 65536),
            TaskContext = TruncateByBytes(sanitizedTaskCtx, 65536),
            TaskPrompt = TruncateByBytes(sanitizedPrompt, 65536),
            AgentResult = sanitizedAgentResult,
            GitEvidence = sanitizedGitEvidence,
            PreviousBrainDecision = original.PreviousBrainDecision,
            RetryCount = original.RetryCount,
            Constraints = new (original.Constraints),
            RequestedAt = original.RequestedAt
        };
    }

    private static string TruncateByBytes(string text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text) || maxBytes <= 0) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes) return text;
        return Encoding.UTF8.GetString(bytes, 0, maxBytes) + "\n... [CONTEXT TRUNCATED]";
    }
}
