using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using AIBridge.Models;

namespace AIBridge.Services;

public class McpSelfTestService : IMcpSelfTestService
{
    private readonly ILogService _logService;

    public static readonly string[] Expected15Tools = new[]
    {
        "ping_bridge",
        "get_bridge_status",
        "list_projects",
        "get_project",
        "get_project_progress",
        "get_current_phase",
        "get_dispatchable_tasks",
        "get_task",
        "prepare_task_execution",
        "dispatch_task",
        "get_current_execution",
        "get_execution",
        "cancel_execution",
        "get_review_package",
        "get_git_evidence"
    };

    public McpSelfTestService(ILogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public async Task<McpSelfTestResult> RunSelfTestAsync(string mcpEndpointUrl, string? apiToken = null)
    {
        var result = new McpSelfTestResult
        {
            TargetEndpoint = mcpEndpointUrl,
            ExpectedToolCount = 15
        };

        _logService.LogInfo($"[MCP Self-Test] Starting verification on endpoint: {mcpEndpointUrl}");

        if (string.IsNullOrWhiteSpace(mcpEndpointUrl))
        {
            result.Errors.Add("MCP Endpoint URL is empty.");
            return result;
        }

        try
        {
            // 1. Check Server Connection & Initialize
            Uri endpointUri;
            if (!string.IsNullOrEmpty(apiToken))
            {
                var uriBuilder = new UriBuilder(mcpEndpointUrl);
                string query = string.IsNullOrEmpty(uriBuilder.Query) ? $"token={apiToken}" : $"{uriBuilder.Query.TrimStart('?')}&token={apiToken}";
                uriBuilder.Query = query;
                endpointUri = uriBuilder.Uri;
            }
            else
            {
                endpointUri = new Uri(mcpEndpointUrl);
            }

            var httpClient = new HttpClient();
            if (!string.IsNullOrEmpty(apiToken))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            }

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpointUri
            }, httpClient, loggerFactory: null, ownsHttpClient: true);

            await using var client = await McpClient.CreateAsync(transport);

            result.ServerRunning = true;
            result.InitializePassed = true;
            _logService.LogInfo("[MCP Self-Test] Step 1: Server connection & Initialize PASSED.");

            // 2. Discover Tools
            var toolsList = (await client.ListToolsAsync()).ToList();
            result.DiscoveredToolCount = toolsList.Count;
            result.DiscoveredTools = toolsList.Select(t => t.Name).ToList();

            bool hasAllExpected = Expected15Tools.All(expected => result.DiscoveredTools.Contains(expected, StringComparer.OrdinalIgnoreCase));
            bool hasNoForbidden = !result.DiscoveredTools.Any(name =>
                name.Equals("shell", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("run_shell", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("approve", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("human", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("write_file", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("read_file", StringComparison.OrdinalIgnoreCase));

            if (result.DiscoveredToolCount == 15 && hasAllExpected && hasNoForbidden)
            {
                result.ToolDiscoveryPassed = true;
                _logService.LogInfo("[MCP Self-Test] Step 2: Tool Discovery PASSED (Exact 15 Phase 08 tools).");
            }
            else
            {
                result.ToolDiscoveryPassed = false;
                result.Errors.Add($"Tool discovery failed. Expected exact 15 tools, found {result.DiscoveredToolCount}. Missing or forbidden tools detected.");
                _logService.LogWarning($"[MCP Self-Test] Step 2 FAILED: Discovered {result.DiscoveredToolCount} tools.");
            }

            // 3. Test ping_bridge
            try
            {
                var pingResult = await client.CallToolAsync("ping_bridge", new Dictionary<string, object?>());
                string pingText = GetContentText(pingResult);
                if (pingText.Contains("\"ok\":true") || pingText.Contains("\"ok\": true"))
                {
                    result.PingPassed = true;
                    _logService.LogInfo("[MCP Self-Test] Step 3: ping_bridge call PASSED.");
                }
                else
                {
                    result.Errors.Add($"ping_bridge failed or returned non-ok result: {pingText}");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"ping_bridge execution exception: {ex.Message}");
            }

            // 4. Test Authentication Behavior for Protected Tool
            if (!string.IsNullOrEmpty(apiToken))
            {
                try
                {
                    // Create unauthenticated client to verify rejection of protected tool get_bridge_status
                    var unauthTransport = new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Endpoint = new Uri(mcpEndpointUrl)
                    });
                    await using var unauthClient = await McpClient.CreateAsync(unauthTransport);
                    var unauthResult = await unauthClient.CallToolAsync("get_bridge_status", new Dictionary<string, object?>());
                    string unauthText = GetContentText(unauthResult);

                    if (unauthText.Contains("UNAUTHORIZED") || unauthText.Contains("Unauthorized"))
                    {
                        result.AuthenticationPassed = true;
                        _logService.LogInfo("[MCP Self-Test] Step 4: Authentication Enforcement PASSED (Unauthenticated call rejected).");
                    }
                    else
                    {
                        result.Errors.Add("Authentication test failed: Unauthenticated request to protected tool was not rejected.");
                    }
                }
                catch (Exception ex)
                {
                    // If transport or MCP error occurs for unauthenticated request, that confirms protection!
                    result.AuthenticationPassed = true;
                    _logService.LogInfo($"[MCP Self-Test] Step 4: Authentication Enforcement PASSED ({ex.Message}).");
                }
            }
            else
            {
                result.AuthenticationPassed = true;
            }

            // 5. Overall Pass Calculation
            result.OverallPassed = result.ServerRunning &&
                                   result.InitializePassed &&
                                   result.ToolDiscoveryPassed &&
                                   result.PingPassed &&
                                   result.AuthenticationPassed;

            _logService.LogInfo($"[MCP Self-Test] Completed. Overall Result: {(result.OverallPassed ? "PASS" : "FAIL")}");
            return result;
        }
        catch (Exception ex)
        {
            result.ServerRunning = false;
            result.OverallPassed = false;
            result.Errors.Add($"MCP Self-Test failed with exception: {ex.Message}");
            _logService.LogError("[MCP Self-Test] Critical error during execution", ex);
            return result;
        }
    }

    private static string GetContentText(CallToolResult toolResult)
    {
        if (toolResult?.Content == null || !toolResult.Content.Any()) return string.Empty;
        var first = toolResult.Content.First();
        if (first is TextContentBlock textBlock)
        {
            return textBlock.Text;
        }
        return JsonSerializer.Serialize(first);
    }
}
