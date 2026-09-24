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

    // --- TASK DEPENDENCY REGRESSION (TEST 12) ---

    [Fact]
    public void Validate_TaskInvalidDependencyReference_ReturnsError()
    {
        var plan = CreateValidPlan();
        plan.Phases[0].Tasks[0].Dependencies.Add("task-non-existent");

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("non-existent dependency"));
    }

    [Fact]
    public void Validate_TaskSelfDependency_ReturnsError()
    {
        var plan = CreateValidPlan();
        plan.Phases[0].Tasks[0].Dependencies.Add(plan.Phases[0].Tasks[0].TaskId);

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("self-dependency"));
    }

    [Fact]
    public void Validate_TaskTwoTaskCycle_ReturnsError()
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

    [Fact]
    public void Validate_TaskLongCycle_ReturnsError()
    {
        var plan = CreateValidPlan();
        plan.Phases[0].Tasks.Add(new TaskPlan { TaskId = "task-01-03", PhaseId = "phase-01", Title = "Task 1.3", Objective = "Obj" });

        // T1 -> T2 -> T3 -> T1
        plan.Phases[0].Tasks[0].Dependencies.Add("task-01-02");
        plan.Phases[0].Tasks[1].Dependencies.Add("task-01-03");
        plan.Phases[0].Tasks[2].Dependencies.Add("task-01-01");

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Dependency cycle detected"));
    }

    // --- PHASE DEPENDENCY TESTS (TEST 6 - TEST 11) ---

    [Fact]
    public void Validate_ValidPhaseDependency_ReturnsIsValidTrue() // TEST 6
    {
        var plan = CreateMultiPhasePlan();
        // phase-02 depends on phase-01
        plan.Phases[1].Dependencies.Add("phase-01");

        var result = _validator.Validate(plan);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MissingPhaseDependency_ReturnsError() // TEST 7
    {
        var plan = CreateMultiPhasePlan();
        // phase-02 depends on non-existent phase-99
        plan.Phases[1].Dependencies.Add("phase-99");

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Phase 'phase-02' references non-existent dependency PhaseId 'phase-99'"));
    }

    [Fact]
    public void Validate_PhaseSelfDependency_ReturnsError() // TEST 8
    {
        var plan = CreateValidPlan();
        // phase-01 depends on phase-01
        plan.Phases[0].Dependencies.Add("phase-01");

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Phase 'phase-01' cannot depend on itself"));
    }

    [Fact]
    public void Validate_TwoPhaseCycle_ReturnsError() // TEST 9
    {
        var plan = CreateMultiPhasePlan();
        // phase-01 -> phase-02
        plan.Phases[0].Dependencies.Add("phase-02");
        // phase-02 -> phase-01
        plan.Phases[1].Dependencies.Add("phase-01");

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Phase dependency cycle detected"));
    }

    [Fact]
    public void Validate_LongPhaseCycle_ReturnsError() // TEST 10
    {
        var plan = CreateThreePhasePlan();
        // phase-01 -> phase-02 -> phase-03 -> phase-01
        plan.Phases[0].Dependencies.Add("phase-02");
        plan.Phases[1].Dependencies.Add("phase-03");
        plan.Phases[2].Dependencies.Add("phase-01");

        var result = _validator.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Phase dependency cycle detected"));
    }

    [Fact]
    public void Validate_AcyclicMultiPhaseGraph_ReturnsIsValidTrue() // TEST 11
    {
        // phase-01
        // phase-02 -> phase-01
        // phase-03 -> phase-01
        // phase-04 -> phase-02, phase-03
        var plan = new ProjectPlan
        {
            ProjectId = "project-dag",
            Name = "DAG Project",
            Goal = "Test DAG",
            Status = ProjectPlanStatus.Draft,
            Phases = new List<PhasePlan>
            {
                new PhasePlan { PhaseId = "phase-01", PhaseNumber = 1, Name = "P1", Objective = "O1", Weight = 1.0, Tasks = new List<TaskPlan> { new TaskPlan { TaskId = "task-01-01", PhaseId = "phase-01", TaskNumber = 1, Title = "T1", Objective = "O1" } } },
                new PhasePlan { PhaseId = "phase-02", PhaseNumber = 2, Name = "P2", Objective = "O2", Weight = 1.0, Dependencies = new List<string> { "phase-01" }, Tasks = new List<TaskPlan> { new TaskPlan { TaskId = "task-02-01", PhaseId = "phase-02", TaskNumber = 1, Title = "T2", Objective = "O2" } } },
                new PhasePlan { PhaseId = "phase-03", PhaseNumber = 3, Name = "P3", Objective = "O3", Weight = 1.0, Dependencies = new List<string> { "phase-01" }, Tasks = new List<TaskPlan> { new TaskPlan { TaskId = "task-03-01", PhaseId = "phase-03", TaskNumber = 1, Title = "T3", Objective = "O3" } } },
                new PhasePlan { PhaseId = "phase-04", PhaseNumber = 4, Name = "P4", Objective = "O4", Weight = 1.0, Dependencies = new List<string> { "phase-02", "phase-03" }, Tasks = new List<TaskPlan> { new TaskPlan { TaskId = "task-04-01", PhaseId = "phase-04", TaskNumber = 1, Title = "T4", Objective = "O4" } } }
            }
        };

        var result = _validator.Validate(plan);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
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

    private static ProjectPlan CreateMultiPhasePlan()
    {
        var plan = CreateValidPlan();
        plan.Phases.Add(new PhasePlan
        {
            PhaseId = "phase-02",
            PhaseNumber = 2,
            Name = "Phase 2",
            Objective = "Objective 2",
            Weight = 1.0,
            Tasks = new List<TaskPlan>
            {
                new TaskPlan
                {
                    TaskId = "task-02-01",
                    PhaseId = "phase-02",
                    TaskNumber = 1,
                    Title = "Task 2.1",
                    Objective = "Objective 2.1"
                }
            }
        });
        return plan;
    }

    private static ProjectPlan CreateThreePhasePlan()
    {
        var plan = CreateMultiPhasePlan();
        plan.Phases.Add(new PhasePlan
        {
            PhaseId = "phase-03",
            PhaseNumber = 3,
            Name = "Phase 3",
            Objective = "Objective 3",
            Weight = 1.0,
            Tasks = new List<TaskPlan>
            {
                new TaskPlan
                {
                    TaskId = "task-03-01",
                    PhaseId = "phase-03",
                    TaskNumber = 1,
                    Title = "Task 3.1",
                    Objective = "Objective 3.1"
                }
            }
        });
        return plan;
    }
}
