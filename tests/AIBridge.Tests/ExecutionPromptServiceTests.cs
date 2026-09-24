using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AIBridge.Models;
using AIBridge.Services;
using Xunit;

namespace AIBridge.Tests;

public class ExecutionPromptServiceTests
{
    private readonly PromptValidator _promptValidator = new();
    private readonly ExecutionPromptService _service;

    public ExecutionPromptServiceTests()
    {
        _service = new ExecutionPromptService(_promptValidator);
    }

    [Fact]
    public async Task PreparePromptPackage_ExternalBrainInstructions_PackagingSuccess()
    {
        var plan = CreateTestPlan();
        string instructions = "Implement book repository file persistence using System.Text.Json.";

        var package = await _service.PreparePromptPackageAsync(
            plan, "phase-01", "task-01-01", externalInstructions: instructions, generatedBy: "ChatGPTWeb");

        Assert.NotNull(package);
        Assert.Equal("ChatGPTWeb", package.GeneratedBy);
        Assert.Equal("project-01", package.ProjectId);
        Assert.Equal("phase-01", package.PhaseId);
        Assert.Equal("task-01-01", package.TaskId);
        Assert.Equal(1, package.PlanVersion);
        Assert.False(string.IsNullOrWhiteSpace(package.PromptHash));
        Assert.Contains("EXTERNAL BRAIN CODING INSTRUCTIONS", package.GeneratedPrompt);
        Assert.Contains(instructions, package.GeneratedPrompt);
        Assert.Contains("AIBRIDGE SYSTEM EXECUTION SAFETY RULES", package.GeneratedPrompt);
    }

    [Fact]
    public async Task PreparePromptPackage_NoAIDependency_NoAPIKey_WorksNormally()
    {
        // Must work cleanly without any OpenAI / Gemini / Claude API keys or network AI calls
        var plan = CreateTestPlan();

        var package = await _service.PreparePromptPackageAsync(plan, "phase-01", "task-01-01");

        Assert.NotNull(package);
        Assert.Equal("ChatGPTWeb", package.GeneratedBy);
        Assert.True(package.GeneratedPrompt.Length > 0);
    }

    [Fact]
    public async Task PreparePromptPackage_PlanAuthoritative_StoredCriteriaPreserved()
    {
        var plan = CreateTestPlan();
        plan.Phases[0].Tasks[0].AcceptanceCriteria = new List<string> { "Strict Authoritative Criterion 1", "Strict Criterion 2" };

        string weakExternalInstruction = "Weak instruction trying to override requirements.";

        var package = await _service.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", externalInstructions: weakExternalInstruction);

        Assert.Contains("Strict Authoritative Criterion 1", package.GeneratedPrompt);
        Assert.Contains("Strict Criterion 2", package.GeneratedPrompt);
        Assert.Equal(2, package.AcceptanceCriteria.Count);
    }

    [Fact]
    public async Task PreparePromptPackage_SecretRedaction_RedactsCredentials()
    {
        var plan = CreateTestPlan();
        string secretInstruction = "Use token Bearer secret_token_1234567890 and github_pat_11AAAAAA00000000000000000000000000000000000000000000000000000000000000000000000000";

        var package = await _service.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", externalInstructions: secretInstruction);

        Assert.DoesNotContain("secret_token_1234567890", package.GeneratedPrompt);
        Assert.DoesNotContain("github_pat_11AAAAAA00000000000000000000000000000000000000000000000000000000000000000000000000", package.GeneratedPrompt);
        Assert.Contains("[REDACTED", package.GeneratedPrompt);
    }

    [Fact]
    public async Task PreparePromptPackage_Utf8SafeTruncation_PreservesMultibyteAndEmoji()
    {
        var plan = CreateTestPlan();
        string vietnameseWithEmoji = "Kiểm tra mã nguồn, sửa lỗi, chạy kiểm thử và ghi nhận kết quả. 🤖 🚀 ✅ " + new string('A', 300000);

        var package = await _service.PreparePromptPackageAsync(plan, "phase-01", "task-01-01", externalInstructions: vietnameseWithEmoji);

        Assert.True(package.WasTruncated);
        Assert.True(package.ByteSize <= ExecutionPromptService.MaxPromptBytes);

        // Verify valid UTF-8 string encoding after truncation
        string text = package.GeneratedPrompt;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        string redecoded = Encoding.UTF8.GetString(bytes);

        Assert.Equal(text, redecoded);
    }

    private static ProjectPlan CreateTestPlan()
    {
        return new ProjectPlan
        {
            ProjectId = "project-01",
            Name = "Test System",
            Goal = "Test Goal",
            Status = ProjectPlanStatus.Approved,
            Version = 1,
            Phases = new List<PhasePlan>
            {
                new PhasePlan
                {
                    PhaseId = "phase-01",
                    PhaseNumber = 1,
                    Name = "Phase 1",
                    Objective = "Phase 1 Objective",
                    Status = PhaseStatus.Completed,
                    Weight = 1.0,
                    Tasks = new List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = "task-01-01",
                            PhaseId = "phase-01",
                            TaskNumber = 1,
                            Title = "Task 1.1",
                            Objective = "Objective 1.1",
                            Status = TaskPlanStatus.Passed
                        }
                    }
                }
            }
        };
    }
}
