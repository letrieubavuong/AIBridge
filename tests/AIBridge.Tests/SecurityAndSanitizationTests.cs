using System;
using System.IO;
using System.Threading.Tasks;
using AIBridge.Infrastructure;
using AIBridge.Models;
using AIBridge.Services;
using Xunit;

namespace AIBridge.Tests;

public class SecurityAndSanitizationTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileProjectPlanStore _store;

    public SecurityAndSanitizationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AIBridge_SecTests_" + Guid.NewGuid().ToString("N"));
        _store = new FileProjectPlanStore(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void SecretRedactor_RedactsSensitiveTokens()
    {
        string inputWithSecrets = "sk-1234567890abcdef1234567890 and Bearer eyJhbGciOiJIUzI1NiJ9.test and ghp_1234567890abcdefghijklmnopqrstuvwxyz";

        var (redacted, modified) = SecretRedactor.Redact(inputWithSecrets);

        Assert.True(modified);
        Assert.DoesNotContain("sk-1234567890abcdef1234567890", redacted);
        Assert.DoesNotContain("ghp_1234567890abcdefghijklmnopqrstuvwxyz", redacted);
        Assert.Contains("[REDACTED_API_KEY]", redacted);
        Assert.Contains("[REDACTED_GITHUB_TOKEN]", redacted);
    }

    [Fact]
    public async Task SavePlanAsync_SanitizesSecretsBeforePersistingToDisk()
    {
        var plan = new ProjectPlan
        {
            ProjectId = "proj-secret-test",
            Name = "Secret Test Project",
            Goal = "Goal with secret sk-1234567890abcdef1234567890",
            Description = "Description with Bearer secrettoken12345678901234567890"
        };

        await _store.SavePlanAsync(plan);

        string planFilePath = Path.Combine(_testDir, plan.ProjectId, "plan.json");
        string rawJsonOnDisk = await File.ReadAllTextAsync(planFilePath);

        Assert.DoesNotContain("sk-1234567890abcdef1234567890", rawJsonOnDisk);
        Assert.DoesNotContain("secrettoken12345678901234567890", rawJsonOnDisk);
        Assert.Contains("[REDACTED_API_KEY]", rawJsonOnDisk);
    }
}
