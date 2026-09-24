using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AIBridge.Infrastructure;
using AIBridge.Models;

namespace AIBridge.Services;

public static class BrainContextSanitizer
{
    public const int MaxContextBytes = 524288; // 512 KB Total Budget

    public static BrainRequest Sanitize(BrainRequest original)
    {
        if (original == null) return new BrainRequest();

        // Step 1: Redact secrets in all text fields (Security First)
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
                StandardOutput = Utf8SafeTruncate(sanitizedOut, 131072, "\n... [STDOUT TRUNCATED]").Text, // 128 KB initial limit
                StandardError = Utf8SafeTruncate(sanitizedErr, 65536, "\n... [STDERR TRUNCATED]").Text,   // 64 KB initial limit
                ErrorMessage = sanitizedErrMsg,
                StartedAt = original.AgentResult.StartedAt,
                CompletedAt = original.AgentResult.CompletedAt
            };
        }

        GitEvidence? sanitizedGitEvidence = null;
        if (original.GitEvidence != null)
        {
            var (sanitizedDiff, _) = SecretRedactor.Redact(original.GitEvidence.Diff ?? string.Empty);
            var (boundedDiff, isDiffTruncated, _, _) = Utf8SafeTruncate(sanitizedDiff, 262144, "\n... [DIFF TRUNCATED]");

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
                CommitList = new List<GitCommitInfo>(original.GitEvidence.CommitList),
                ChangedFiles = new List<ChangedFileInfo>(original.GitEvidence.ChangedFiles),
                CommittedFiles = new List<ChangedFileInfo>(original.GitEvidence.CommittedFiles),
                UncommittedFiles = new List<ChangedFileInfo>(original.GitEvidence.UncommittedFiles),
                PreExistingChanges = original.GitEvidence.PreExistingChanges,
                Diff = boundedDiff,
                DiffTruncated = original.GitEvidence.DiffTruncated || isDiffTruncated,
                ContainsRedactions = original.GitEvidence.ContainsRedactions,
                PushState = original.GitEvidence.PushState,
                RemoteHeadSha = original.GitEvidence.RemoteHeadSha,
                GitHubOwner = original.GitEvidence.GitHubOwner,
                GitHubRepository = original.GitEvidence.GitHubRepository,
                Warnings = new List<string>(original.GitEvidence.Warnings),
                Errors = new List<string>(original.GitEvidence.Errors),
                CapturedAt = original.GitEvidence.CapturedAt
            };
        }

        var sanitizedReq = new BrainRequest
        {
            RequestId = original.RequestId,
            ProjectId = original.ProjectId,
            PhaseId = original.PhaseId,
            TaskId = original.TaskId,
            RequestType = original.RequestType,
            ProjectContext = Utf8SafeTruncate(sanitizedProj, 65536, "\n... [PROJECT CTX TRUNCATED]").Text,
            PhaseContext = Utf8SafeTruncate(sanitizedPhase, 65536, "\n... [PHASE CTX TRUNCATED]").Text,
            TaskContext = Utf8SafeTruncate(sanitizedTaskCtx, 65536, "\n... [TASK CTX TRUNCATED]").Text,
            TaskPrompt = Utf8SafeTruncate(sanitizedPrompt, 65536, "\n... [PROMPT TRUNCATED]").Text,
            AgentResult = sanitizedAgentResult,
            GitEvidence = sanitizedGitEvidence,
            PreviousBrainDecision = original.PreviousBrainDecision,
            RetryCount = original.RetryCount,
            Constraints = new Dictionary<string, string>(original.Constraints),
            RequestedAt = original.RequestedAt
        };

        // Step 2: Global 512 KB Budget Enforcement across the ENTIRE context
        EnforceGlobalBudget(sanitizedReq, MaxContextBytes);

        return sanitizedReq;
    }

    private static void EnforceGlobalBudget(BrainRequest req, int maxBytes)
    {
        while (CalculateContextSizeBytes(req) > maxBytes)
        {
            int currentBytes = CalculateContextSizeBytes(req);
            int excess = currentBytes - maxBytes;

            // Priority 12: Truncate GitEvidence.Diff first
            if (req.GitEvidence != null && !string.IsNullOrEmpty(req.GitEvidence.Diff))
            {
                int diffBytes = GetUtf8ByteCount(req.GitEvidence.Diff);
                int targetDiffBytes = Math.Max(0, diffBytes - excess);
                var (newDiff, wasTruncated, _, _) = Utf8SafeTruncate(req.GitEvidence.Diff, targetDiffBytes, "\n... [DIFF TRUNCATED]");
                req.GitEvidence.Diff = newDiff;
                req.GitEvidence.DiffTruncated = true;

                if (CalculateContextSizeBytes(req) <= maxBytes) break;
            }

            excess = CalculateContextSizeBytes(req) - maxBytes;

            // Priority 11: Truncate AgentResult.StandardOutput next
            if (req.AgentResult != null && !string.IsNullOrEmpty(req.AgentResult.StandardOutput))
            {
                int outBytes = GetUtf8ByteCount(req.AgentResult.StandardOutput);
                int targetOutBytes = Math.Max(0, outBytes - excess);
                var (newOut, _, _, _) = Utf8SafeTruncate(req.AgentResult.StandardOutput, targetOutBytes, "\n... [STDOUT TRUNCATED]");
                req.AgentResult.StandardOutput = newOut;

                if (CalculateContextSizeBytes(req) <= maxBytes) break;
            }

            excess = CalculateContextSizeBytes(req) - maxBytes;

            // Priority 10: Truncate ProjectContext
            if (!string.IsNullOrEmpty(req.ProjectContext))
            {
                int projBytes = GetUtf8ByteCount(req.ProjectContext);
                int targetProjBytes = Math.Max(0, projBytes - excess);
                var (newProj, _, _, _) = Utf8SafeTruncate(req.ProjectContext, targetProjBytes, "\n... [PROJECT CTX TRUNCATED]");
                req.ProjectContext = newProj;

                if (CalculateContextSizeBytes(req) <= maxBytes) break;
            }

            excess = CalculateContextSizeBytes(req) - maxBytes;

            // Priority 9: Truncate PhaseContext
            if (!string.IsNullOrEmpty(req.PhaseContext))
            {
                int phaseBytes = GetUtf8ByteCount(req.PhaseContext);
                int targetPhaseBytes = Math.Max(0, phaseBytes - excess);
                var (newPhase, _, _, _) = Utf8SafeTruncate(req.PhaseContext, targetPhaseBytes, "\n... [PHASE CTX TRUNCATED]");
                req.PhaseContext = newPhase;

                if (CalculateContextSizeBytes(req) <= maxBytes) break;
            }

            excess = CalculateContextSizeBytes(req) - maxBytes;

            // Priority 8: Truncate TaskContext
            if (!string.IsNullOrEmpty(req.TaskContext))
            {
                int taskBytes = GetUtf8ByteCount(req.TaskContext);
                int targetTaskBytes = Math.Max(0, taskBytes - excess);
                var (newTaskCtx, _, _, _) = Utf8SafeTruncate(req.TaskContext, targetTaskBytes, "\n... [TASK CTX TRUNCATED]");
                req.TaskContext = newTaskCtx;

                if (CalculateContextSizeBytes(req) <= maxBytes) break;
            }

            excess = CalculateContextSizeBytes(req) - maxBytes;

            // Priority 7: Trim GitEvidence ChangedFiles / CommitList if needed
            if (req.GitEvidence != null && (req.GitEvidence.ChangedFiles.Count > 5 || req.GitEvidence.CommitList.Count > 5))
            {
                req.GitEvidence.ChangedFiles = req.GitEvidence.ChangedFiles.Take(5).ToList();
                req.GitEvidence.CommitList = req.GitEvidence.CommitList.Take(5).ToList();

                if (CalculateContextSizeBytes(req) <= maxBytes) break;
            }

            excess = CalculateContextSizeBytes(req) - maxBytes;

            // Priority 5: Truncate AgentResult.StandardError if needed
            if (req.AgentResult != null && !string.IsNullOrEmpty(req.AgentResult.StandardError))
            {
                int errBytes = GetUtf8ByteCount(req.AgentResult.StandardError);
                int targetErrBytes = Math.Max(0, errBytes - excess);
                var (newErr, _, _, _) = Utf8SafeTruncate(req.AgentResult.StandardError, targetErrBytes, "\n... [STDERR TRUNCATED]");
                req.AgentResult.StandardError = newErr;

                if (CalculateContextSizeBytes(req) <= maxBytes) break;
            }

            // Defensive fallback: if still exceeding maxBytes, trim TaskPrompt
            excess = CalculateContextSizeBytes(req) - maxBytes;
            if (excess > 0 && !string.IsNullOrEmpty(req.TaskPrompt))
            {
                int promptBytes = GetUtf8ByteCount(req.TaskPrompt);
                int targetPromptBytes = Math.Max(512, promptBytes - excess);
                var (newPrompt, _, _, _) = Utf8SafeTruncate(req.TaskPrompt, targetPromptBytes, "\n... [PROMPT TRUNCATED]");
                req.TaskPrompt = newPrompt;
            }

            break; // Safety break
        }
    }

    public static int CalculateContextSizeBytes(BrainRequest req)
    {
        if (req == null) return 0;
        int bytes = 0;

        bytes += GetUtf8ByteCount(req.RequestId);
        bytes += GetUtf8ByteCount(req.ProjectId);
        bytes += GetUtf8ByteCount(req.PhaseId);
        bytes += GetUtf8ByteCount(req.TaskId);
        bytes += GetUtf8ByteCount(req.RequestType.ToString());
        bytes += GetUtf8ByteCount(req.PreviousBrainDecision?.ToString());
        bytes += GetUtf8ByteCount(req.TaskPrompt);
        bytes += GetUtf8ByteCount(req.ProjectContext);
        bytes += GetUtf8ByteCount(req.PhaseContext);
        bytes += GetUtf8ByteCount(req.TaskContext);

        if (req.Constraints != null)
        {
            foreach (var kvp in req.Constraints)
            {
                bytes += GetUtf8ByteCount(kvp.Key);
                bytes += GetUtf8ByteCount(kvp.Value);
            }
        }

        if (req.AgentResult != null)
        {
            bytes += GetUtf8ByteCount(req.AgentResult.ErrorMessage);
            bytes += GetUtf8ByteCount(req.AgentResult.StandardError);
            bytes += GetUtf8ByteCount(req.AgentResult.StandardOutput);
        }

        if (req.GitEvidence != null)
        {
            bytes += GetUtf8ByteCount(req.GitEvidence.Diff);
            bytes += GetUtf8ByteCount(req.GitEvidence.NewCommitSha);
            bytes += GetUtf8ByteCount(req.GitEvidence.NewCommitMessage);
            bytes += GetUtf8ByteCount(req.GitEvidence.CommitRange);
            if (req.GitEvidence.ChangedFiles != null)
            {
                foreach (var cf in req.GitEvidence.ChangedFiles)
                {
                    bytes += GetUtf8ByteCount(cf.Path);
                    bytes += GetUtf8ByteCount(cf.Status);
                }
            }
            if (req.GitEvidence.CommitList != null)
            {
                foreach (var c in req.GitEvidence.CommitList)
                {
                    bytes += GetUtf8ByteCount(c.Sha);
                    bytes += GetUtf8ByteCount(c.Message);
                }
            }
            if (req.GitEvidence.Warnings != null)
            {
                foreach (var w in req.GitEvidence.Warnings) bytes += GetUtf8ByteCount(w);
            }
            if (req.GitEvidence.Errors != null)
            {
                foreach (var e in req.GitEvidence.Errors) bytes += GetUtf8ByteCount(e);
            }
        }

        return bytes;
    }

    public static (string Text, bool WasTruncated, int OriginalBytes, int RetainedBytes) Utf8SafeTruncate(string? text, int maxBytes, string marker = "")
    {
        if (string.IsNullOrEmpty(text) || maxBytes <= 0)
        {
            int orig = GetUtf8ByteCount(text);
            return (string.Empty, !string.IsNullOrEmpty(text), orig, 0);
        }

        int totalBytes = GetUtf8ByteCount(text);
        if (totalBytes <= maxBytes)
        {
            return (text, false, totalBytes, totalBytes);
        }

        int markerBytes = GetUtf8ByteCount(marker);
        if (maxBytes <= markerBytes)
        {
            var markerRawBytes = Encoding.UTF8.GetBytes(marker);
            int safeMarkerCut = CutUtf8Bytes(markerRawBytes, maxBytes);
            string safeMarker = Encoding.UTF8.GetString(markerRawBytes, 0, safeMarkerCut);
            int safeMarkerLen = GetUtf8ByteCount(safeMarker);
            return (safeMarker, true, totalBytes, safeMarkerLen);
        }

        int allowedTextBytes = maxBytes - markerBytes;
        byte[] rawBytes = Encoding.UTF8.GetBytes(text);
        int cutIndex = CutUtf8Bytes(rawBytes, allowedTextBytes);

        string truncatedText = Encoding.UTF8.GetString(rawBytes, 0, cutIndex) + marker;
        int retainedBytes = GetUtf8ByteCount(truncatedText);

        return (truncatedText, true, totalBytes, retainedBytes);
    }

    private static int CutUtf8Bytes(byte[] bytes, int maxAllowed)
    {
        if (maxAllowed >= bytes.Length) return bytes.Length;
        if (maxAllowed <= 0) return 0;

        int cut = maxAllowed;
        if ((bytes[cut] & 0xC0) == 0x80)
        {
            while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
            {
                cut--;
            }
        }

        return cut;
    }

    private static int GetUtf8ByteCount(string? str)
    {
        return string.IsNullOrEmpty(str) ? 0 : Encoding.UTF8.GetByteCount(str);
    }
}
