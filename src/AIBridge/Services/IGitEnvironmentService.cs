using System.Threading.Tasks;
using AIBridge.Models;

namespace AIBridge.Services;

public interface IGitEnvironmentService
{
    string ResolveGitExecutable(string? configuredPath = null);
    Task<GitEnvironmentInfo> DetectAndVerifyEnvironmentAsync(string? workspacePath = null, string? configuredGitPath = null);
}
