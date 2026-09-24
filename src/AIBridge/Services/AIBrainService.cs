using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public class AIBrainService : IAIBrainService
{
    private readonly ILogService _logService;
    private readonly IConfigService _configService;
    private readonly IAIBrainProviderRegistry _registry;
    private readonly SemaphoreSlim _concurrencyLock = new(1, 1);
    private readonly List<BrainDecisionRecord> _history = new();
    private readonly object _historyLock = new();

    private BrainState _currentState = BrainState.Ready;

    public AIBrainService(
        ILogService logService,
        IConfigService configService,
        IAIBrainProviderRegistry registry)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public BrainState GetCurrentState() => _currentState;

    public BrainProviderDescriptor? GetActiveProviderDescriptor()
    {
        var config = _configService.LoadConfig();
        var providerId = string.IsNullOrWhiteSpace(config.BrainProvider) ? "mock" : config.BrainProvider;
        var provider = _registry.GetProvider(providerId);
        return provider?.Descriptor;
    }

    public IReadOnlyList<BrainDecisionRecord> GetDecisionHistory()
    {
        lock (_historyLock)
        {
            return _history.ToList().AsReadOnly();
        }
    }

    public async Task<bool> TestActiveProviderAsync(CancellationToken cancellationToken = default)
    {
        var config = _configService.LoadConfig();
        if (!config.BrainEnabled) return false;

        var providerId = string.IsNullOrWhiteSpace(config.BrainProvider) ? "mock" : config.BrainProvider;
        var provider = _registry.GetProvider(providerId);
        if (provider == null) return false;

        try
        {
            return await provider.TestConnectionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logService.LogError($"Provider '{providerId}' connection test failed", ex);
            return false;
        }
    }

    public async Task<BrainResponse> AnalyzeAsync(BrainRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var config = _configService.LoadConfig();
        if (!config.BrainEnabled)
        {
            _logService.LogWarning("BrainService analysis requested but Brain is disabled in configuration.");
            return CreateErrorResponse(request, "BRAIN_DISABLED", "AI Brain is currently disabled in configuration.", BrainDecision.BLOCKED);
        }

        var providerId = string.IsNullOrWhiteSpace(config.BrainProvider) ? "mock" : config.BrainProvider;
        var provider = _registry.GetProvider(providerId);
        if (provider == null)
        {
            _logService.LogWarning($"Active AI Brain provider '{providerId}' not found in registry.");
            return CreateErrorResponse(request, "PROVIDER_NOT_FOUND", $"Configured provider '{providerId}' is not registered.", BrainDecision.BLOCKED);
        }

        // Bounded concurrency check (MaxConcurrentBrainRequests = 1)
        bool acquired = await _concurrencyLock.WaitAsync(0, cancellationToken);
        if (!acquired)
        {
            _logService.LogWarning("Concurrent Brain request rejected; another Brain request is active.");
            return CreateErrorResponse(request, "RATE_LIMITED", "Another Brain request is currently in progress.", BrainDecision.BLOCKED);
        }

        _currentState = BrainState.Thinking;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Sanitize context before sending to provider
            var sanitizedRequest = BrainContextSanitizer.Sanitize(request);

            int timeoutSec = config.BrainTimeoutSeconds > 0 ? config.BrainTimeoutSeconds : 120;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            _logService.LogInfo($"Invoking AI Brain provider '{provider.Descriptor.DisplayName}' for RequestType={sanitizedRequest.RequestType}...");

            var response = await provider.AnalyzeAsync(sanitizedRequest, linkedCts.Token);
            stopwatch.Stop();
            response.DurationMs = stopwatch.ElapsedMilliseconds;

            _currentState = BrainState.Ready;
            RecordHistory(sanitizedRequest, response, success: true);

            _logService.LogInfo($"AI Brain decision returned: {response.Decision} (Duration: {response.DurationMs}ms)");
            return response;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _currentState = BrainState.Ready;
            var isUserCancel = cancellationToken.IsCancellationRequested;
            var errCode = isUserCancel ? "CANCELLED" : "TIMEOUT";
            var errMsg = isUserCancel ? "Brain request was cancelled by user." : $"Brain request timed out after {config.BrainTimeoutSeconds} seconds.";

            var errResp = CreateErrorResponse(request, errCode, errMsg, BrainDecision.BLOCKED, stopwatch.ElapsedMilliseconds);
            RecordHistory(request, errResp, success: false);
            return errResp;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _currentState = BrainState.Ready;
            _logService.LogError($"Unhandled exception in AI Brain provider '{providerId}'", ex);

            var errResp = CreateErrorResponse(request, "PROVIDER_ERROR", $"Provider exception: {ex.Message}", BrainDecision.BLOCKED, stopwatch.ElapsedMilliseconds);
            RecordHistory(request, errResp, success: false);
            return errResp;
        }
        finally
        {
            _concurrencyLock.Release();
        }
    }

    private void RecordHistory(BrainRequest req, BrainResponse resp, bool success)
    {
        lock (_historyLock)
        {
            _history.Add(new BrainDecisionRecord
            {
                RequestId = req.RequestId,
                RequestType = req.RequestType,
                ProviderId = resp.ProviderId,
                Model = resp.Model,
                StartedAt = req.RequestedAt,
                CompletedAt = DateTime.Now,
                DurationMs = resp.DurationMs,
                Decision = resp.Decision,
                Summary = resp.Summary,
                Success = success,
                ErrorCode = resp.ErrorCode
            });

            // Enforce bounded history size (max 100 items)
            if (_history.Count > 100)
            {
                _history.RemoveAt(0);
            }
        }
    }

    private static BrainResponse CreateErrorResponse(BrainRequest req, string errorCode, string message, BrainDecision defaultDecision, long durationMs = 0)
    {
        return new BrainResponse
        {
            RequestId = req.RequestId,
            Decision = defaultDecision,
            Reason = message,
            Summary = $"ERROR [{errorCode}]: {message}",
            ErrorCode = errorCode,
            ErrorMessage = message,
            DurationMs = durationMs
        };
    }
}
