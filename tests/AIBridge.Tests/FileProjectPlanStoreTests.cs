using System;
using System.IO;
using System.Threading.Tasks;
using AIBridge.Models;
using AIBridge.Services;
using Xunit;

namespace AIBridge.Tests;

public class FileProjectPlanStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileProjectPlanStore _store;

    public FileProjectPlanStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AIBridge_Tests_" + Guid.NewGuid().ToString("N"));
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
    public async Task SaveAndLoad_ValidPlan_SurvivesRestart()
    {
        var plan = new ProjectPlan
        {
            ProjectId = "proj-restart-test",
            Name = "Book Management System",
            Goal = "Manage books locally",
            Status = ProjectPlanStatus.AwaitingApproval,
            Version = 1,
            Phases = new System.Collections.Generic.List<PhasePlan>
            {
                new PhasePlan
                {
                    PhaseId = "phase-01",
                    PhaseNumber = 1,
                    Name = "Phase 1",
                    Objective = "Setup",
                    Tasks = new System.Collections.Generic.List<TaskPlan>
                    {
                        new TaskPlan { TaskId = "task-01-01", PhaseId = "phase-01", TaskNumber = 1, Title = "Setup DB", Objective = "Init SQLite" }
                    }
                }
            }
        };

        await _store.SavePlanAsync(plan);

        // Simulate app restart by creating a new store instance pointing to same path
        var freshStore = new FileProjectPlanStore(_testDir);
        var loadedPlan = await freshStore.LoadPlanAsync(plan.ProjectId);

        Assert.NotNull(loadedPlan);
        Assert.Equal(plan.ProjectId, loadedPlan.ProjectId);
        Assert.Equal(plan.Name, loadedPlan.Name);
        Assert.Equal(plan.Version, loadedPlan.Version);
        Assert.Equal(plan.Status, loadedPlan.Status);
        Assert.Single(loadedPlan.Phases);
        Assert.Single(loadedPlan.Phases[0].Tasks);
        Assert.Equal("task-01-01", loadedPlan.Phases[0].Tasks[0].TaskId);
    }

    [Fact]
    public async Task Save_AtomicWrite_DoesNotLeaveCorruptedFiles()
    {
        var plan = new ProjectPlan
        {
            ProjectId = "proj-atomic-test",
            Name = "Atomic Write Test",
            Goal = "Test atomic safe save"
        };

        await _store.SavePlanAsync(plan);

        string planPath = Path.Combine(_testDir, plan.ProjectId, "plan.json");
        Assert.True(File.Exists(planPath));
        
        // Verify no orphaned .tmp files remain in project directory
        var tmpFiles = Directory.GetFiles(Path.Combine(_testDir, plan.ProjectId), "*.tmp_*");
        Assert.Empty(tmpFiles);
    }

    [Fact]
    public async Task Load_CorruptedFile_DoesNotCrashApp()
    {
        string projId = "proj-corrupted";
        string projDir = Path.Combine(_testDir, projId);
        Directory.CreateDirectory(projDir);
        string planPath = Path.Combine(projDir, "plan.json");

        // Write invalid JSON
        await File.WriteAllTextAsync(planPath, "{ invalid json content... ");

        // LoadPlanAsync should catch JsonException and throw controlled InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _store.LoadPlanAsync(projId);
        });

        // Original damaged file preserved for diagnosis
        Assert.True(File.Exists(planPath));
    }
}
