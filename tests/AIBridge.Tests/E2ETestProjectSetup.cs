using System;
using System.Collections.Generic;
using System.IO;
using AIBridge.Models;

namespace AIBridge.Tests;

public static class E2ETestProjectSetup
{
    public static ProjectPlan CreateMiniCalculatorPlan()
    {
        return new ProjectPlan
        {
            ProjectId = "AIBridge-E2E-Test",
            Name = "Mini Calculator E2E Test",
            Goal = "Build a Mini Calculator with unit tests to demonstrate end-to-end ChatGPT Web MCP orchestration",
            Status = ProjectPlanStatus.Approved,
            Version = 1,
            Phases = new List<PhasePlan>
            {
                new PhasePlan
                {
                    PhaseId = "phase-A",
                    PhaseNumber = 1,
                    Name = "Phase A — Foundation",
                    Objective = "Create .NET Console solution and base Calculator class",
                    Status = PhaseStatus.Running,
                    Tasks = new List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = "task-A1",
                            PhaseId = "phase-A",
                            TaskNumber = 1,
                            Title = "Create Console Project",
                            Objective = "Create .NET Console solution and MiniCalculator project structure",
                            Status = TaskPlanStatus.NotStarted,
                            AcceptanceCriteria = new List<string> { "dotnet build succeeds" },
                            RequiresHumanApproval = false
                        },
                        new TaskPlan
                        {
                            TaskId = "task-A2",
                            PhaseId = "phase-A",
                            TaskNumber = 2,
                            Title = "Create Calculator Base Class",
                            Objective = "Create Calculator.cs with class definition",
                            Status = TaskPlanStatus.NotStarted,
                            AcceptanceCriteria = new List<string> { "Calculator.cs exists", "dotnet build succeeds" },
                            RequiresHumanApproval = false
                        }
                    }
                },
                new PhasePlan
                {
                    PhaseId = "phase-B",
                    PhaseNumber = 2,
                    Name = "Phase B — Features",
                    Objective = "Implement arithmetic operations Add, Subtract, Multiply, and Divide with Vietnamese error message",
                    Status = PhaseStatus.NotStarted,
                    Dependencies = new List<string> { "phase-A" },
                    Tasks = new List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = "task-B1",
                            PhaseId = "phase-B",
                            TaskNumber = 1,
                            Title = "Implement Add",
                            Objective = "Add method public double Add(double a, double b) returning a + b",
                            Status = TaskPlanStatus.NotStarted,
                            AcceptanceCriteria = new List<string> { "Add method works correctly" },
                            RequiresHumanApproval = false
                        },
                        new TaskPlan
                        {
                            TaskId = "task-B2",
                            PhaseId = "phase-B",
                            TaskNumber = 2,
                            Title = "Implement Subtract",
                            Objective = "Add method public double Subtract(double a, double b) returning a - b",
                            Status = TaskPlanStatus.NotStarted,
                            AcceptanceCriteria = new List<string> { "Subtract method works correctly" },
                            RequiresHumanApproval = false
                        },
                        new TaskPlan
                        {
                            TaskId = "task-B3",
                            PhaseId = "phase-B",
                            TaskNumber = 3,
                            Title = "Implement Multiply",
                            Objective = "Add method public double Multiply(double a, double b) returning a * b",
                            Status = TaskPlanStatus.NotStarted,
                            AcceptanceCriteria = new List<string> { "Multiply method works correctly" },
                            RequiresHumanApproval = false
                        },
                        new TaskPlan
                        {
                            TaskId = "task-B4",
                            PhaseId = "phase-B",
                            TaskNumber = 4,
                            Title = "Implement Divide (Vietnamese Error)",
                            Objective = "Implement Divide(double a, double b). If b = 0, throw ArgumentException with exact text 'Không thể chia cho số 0'",
                            Status = TaskPlanStatus.NotStarted,
                            AcceptanceCriteria = new List<string> { "Divide works for non-zero", "Throws exception containing 'Không thể chia cho số 0' when b = 0" },
                            RequiresHumanApproval = true
                        }
                    }
                },
                new PhasePlan
                {
                    PhaseId = "phase-C",
                    PhaseNumber = 3,
                    Name = "Phase C — Tests",
                    Objective = "Create xUnit test project and verify all calculator operations",
                    Status = PhaseStatus.NotStarted,
                    Dependencies = new List<string> { "phase-B" },
                    Tasks = new List<TaskPlan>
                    {
                        new TaskPlan
                        {
                            TaskId = "task-C1",
                            PhaseId = "phase-C",
                            TaskNumber = 1,
                            Title = "Create Test Project",
                            Objective = "Create MiniCalculator.Tests xUnit test project",
                            Status = TaskPlanStatus.NotStarted,
                            AcceptanceCriteria = new List<string> { "MiniCalculator.Tests project exists" },
                            RequiresHumanApproval = false
                        },
                        new TaskPlan
                        {
                            TaskId = "task-C2",
                            PhaseId = "phase-C",
                            TaskNumber = 2,
                            Title = "Write Calculator Unit Tests",
                            Objective = "Write tests for Add, Subtract, Multiply, and Divide including zero check",
                            Status = TaskPlanStatus.NotStarted,
                            AcceptanceCriteria = new List<string> { "CalculatorTests.cs contains tests for all operations" },
                            RequiresHumanApproval = false
                        },
                        new TaskPlan
                        {
                            TaskId = "task-C3",
                            PhaseId = "phase-C",
                            TaskNumber = 3,
                            Title = "Run All Tests",
                            Objective = "Run dotnet test and verify all tests pass",
                            Status = TaskPlanStatus.NotStarted,
                            AcceptanceCriteria = new List<string> { "dotnet test PASS" },
                            RequiresHumanApproval = false
                        }
                    }
                }
            }
        };
    }
}
