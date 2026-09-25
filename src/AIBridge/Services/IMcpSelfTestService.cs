using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IMcpSelfTestService
{
    Task<McpSelfTestResult> RunSelfTestAsync(string mcpEndpointUrl, string? apiToken = null);
}
