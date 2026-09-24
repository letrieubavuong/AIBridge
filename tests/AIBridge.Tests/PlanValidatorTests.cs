using System.Collections.Generic;
using AIBridge.Models;
using AIBridge.Services;
using Xunit;

namespace AIBridge.Tests;

public class PlanValidatorTests
{
    private readonly PlanValidator _validator = new();

    [Fact]
    public void Validate_ValidPlan_ReturnsIsValidTrue()
    {
        var plan = CreateValidPlan();
        var result = _validator.Validate(plan);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MissingProjectId_ReturnsError()
    {
        var plan = CreateValidPlan();
        plan.ProjectId = "";

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ProjectId"));
    }

    [Fact]
    public void Validate_DuplicateTaskId_ReturnsError()
    {
        var plan = CreateValidPlan();
        plan.Phases[0].Tasks.Add(new TaskPlan
        {
            TaskId = "task-01-01", // Duplicate ID
            PhaseId = "phase-01",
            Title = "Duplicate Task",
            Objective = "Test duplicate handling"
        });

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate TaskId"));
    }

    [Fact]
    public void Validate_InvalidDependencyReference_ReturnsError()
    {
        var plan = CreateValidPlan();
        plan.Phases[0].Tasks[0].Dependencies.Add("task-non-existent");

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("non-existent dependency"));
    }

    [Fact]
    public void Validate_SelfDependency_ReturnsError()
    {
        var plan = CreateValidPlan();
        plan.Phases[0].Tasks[0].Dependencies.Add(plan.Phases[0].Tasks[0].TaskId);

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("self-dependency"));
    }

    [Fact]
    public void Validate_DependencyCycle_ReturnsError()
    {
        var plan = CreateValidPlan();
        // Task 1.1 -> Task 1.2
        plan.Phases[0].Tasks[0].Dependencies.Add("task-01-02");
        // Task 1.2 -> Task 1.1 (Cycle!)
        plan.Phases[0].Tasks[1].Dependencies.Add("task-01-01");

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Dependency cycle detected"));
    }

    private static ProjectPlan CreateValidPlan()
    {
        return new ProjectPlan
        {
            ProjectId = "project-test1",
            Name = "Test Project",
            Goal = "Test Goal",
            Status = ProjectPlanStatus.Draft,
            Phases = new List<PhasePlan>
            {
                new PhasePlan
                {
                    PhaseId = "phase-01",
                    PhaseNumber = 1,
                    Name = "Phase 1",
                    Objective = "Objective 1",
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
                            MaxRetries = 3
                        },
                        new TaskPlan
                        {
                            TaskId = "task-01-02",
                            PhaseId = "phase-01",
                            TaskNumber = 2,
                            Title = "Task 1.2",
                            Objective = "Objective 1.2",
                            MaxRetries = 3
                        }
                    }
                }
            }
        };
    }
}
