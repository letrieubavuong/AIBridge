using System;
using System.Collections.Generic;
using System.Linq;
using AIBridge.Models;

namespace AIBridge.Services;

public class PlanValidator : IPlanValidator
{
    public PlanningValidationResult Validate(ProjectPlan plan)
    {
        var result = new PlanningValidationResult();

        if (plan == null)
        {
            result.IsValid = false;
            result.Errors.Add("Project plan cannot be null.");
            return result;
        }

        // 1. Project-level validations
        if (string.IsNullOrWhiteSpace(plan.ProjectId))
        {
            result.Errors.Add("Project must have a non-empty ProjectId.");
        }

        if (string.IsNullOrWhiteSpace(plan.Name))
        {
            result.Errors.Add("Project must have a non-empty Name.");
        }

        if (plan.Phases == null || plan.Phases.Count == 0)
        {
            result.Errors.Add("Project must contain at least one phase.");
            result.IsValid = false;
            return result;
        }

        // 2. Phase & Task Collection & Duplicate Tracking
        var phaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allTasks = new List<TaskPlan>();

        foreach (var phase in plan.Phases)
        {
            if (string.IsNullOrWhiteSpace(phase.PhaseId))
            {
                result.Errors.Add($"Phase '{phase.Name}' has a missing or empty PhaseId.");
            }
            else if (!phaseIds.Add(phase.PhaseId))
            {
                result.Errors.Add($"Duplicate PhaseId found: '{phase.PhaseId}'.");
            }

            if (phase.PhaseNumber <= 0)
            {
                result.Errors.Add($"Phase '{phase.PhaseId}' has invalid PhaseNumber: {phase.PhaseNumber}. Must be > 0.");
            }

            if (string.IsNullOrWhiteSpace(phase.Name))
            {
                result.Errors.Add($"Phase '{phase.PhaseId}' must have a non-empty Name.");
            }

            if (string.IsNullOrWhiteSpace(phase.Objective))
            {
                result.Errors.Add($"Phase '{phase.PhaseId}' must have a non-empty Objective.");
            }

            if (phase.Weight <= 0)
            {
                result.Errors.Add($"Phase '{phase.PhaseId}' has invalid Weight: {phase.Weight}. Weight must be > 0.");
            }

            if (phase.Tasks == null || phase.Tasks.Count == 0)
            {
                result.Errors.Add($"Phase '{phase.PhaseId}' must contain at least one task.");
            }
            else
            {
                foreach (var task in phase.Tasks)
                {
                    allTasks.Add(task);

                    if (string.IsNullOrWhiteSpace(task.TaskId))
                    {
                        result.Errors.Add($"Task '{task.Title}' in phase '{phase.PhaseId}' has a missing or empty TaskId.");
                    }
                    else if (!taskIds.Add(task.TaskId))
                    {
                        result.Errors.Add($"Duplicate TaskId found: '{task.TaskId}'.");
                    }

                    if (string.IsNullOrWhiteSpace(task.Title))
                    {
                        result.Errors.Add($"Task '{task.TaskId}' must have a non-empty Title.");
                    }

                    if (string.IsNullOrWhiteSpace(task.Objective))
                    {
                        result.Errors.Add($"Task '{task.TaskId}' must have a non-empty Objective.");
                    }

                    if (string.IsNullOrWhiteSpace(task.PhaseId))
                    {
                        result.Errors.Add($"Task '{task.TaskId}' is missing PhaseId reference.");
                    }
                    else if (!string.Equals(task.PhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Errors.Add($"Task '{task.TaskId}' has PhaseId '{task.PhaseId}' which does not match parent phase '{phase.PhaseId}'.");
                    }

                    if (task.MaxRetries < 0)
                    {
                        result.Errors.Add($"Task '{task.TaskId}' has invalid MaxRetries: {task.MaxRetries}. Must be >= 0.");
                    }

                    if (task.TaskNumber <= 0)
                    {
                        result.Warnings.Add($"Task '{task.TaskId}' has non-positive TaskNumber: {task.TaskNumber}.");
                    }
                }
            }

        }

        // 3. Phase Dependency References and Self-Dependency Checks
        foreach (var phase in plan.Phases)
        {
            if (phase.Dependencies != null && phase.Dependencies.Count > 0)
            {
                foreach (var depPhaseId in phase.Dependencies)
                {
                    if (string.Equals(depPhaseId, phase.PhaseId, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Errors.Add($"Phase '{phase.PhaseId}' cannot depend on itself.");
                    }
                    else if (!phaseIds.Contains(depPhaseId))
                    {
                        result.Errors.Add($"Phase '{phase.PhaseId}' references non-existent dependency PhaseId '{depPhaseId}'.");
                    }
                }
            }
        }

        // 4. Task Dependency References and Self-Dependency Checks
        foreach (var task in allTasks)
        {
            if (task.Dependencies != null && task.Dependencies.Count > 0)
            {
                foreach (var depId in task.Dependencies)
                {
                    if (string.Equals(depId, task.TaskId, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Errors.Add($"Task '{task.TaskId}' has a direct self-dependency on itself.");
                    }
                    else if (!taskIds.Contains(depId))
                    {
                        result.Errors.Add($"Task '{task.TaskId}' references non-existent dependency TaskId '{depId}'.");
                    }
                }
            }
        }

        // 5. Dependency Cycle Detection (Phase Graph)
        if (HasPhaseDependencyCycle(plan.Phases, out var phaseCycleErrors))
        {
            foreach (var cycleError in phaseCycleErrors)
            {
                result.Errors.Add(cycleError);
            }
        }

        // 6. Dependency Cycle Detection (Task Graph)
        if (HasTaskDependencyCycle(allTasks, out var cycleErrors))
        {
            foreach (var cycleError in cycleErrors)
            {
                result.Errors.Add(cycleError);
            }
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    private static bool HasTaskDependencyCycle(List<TaskPlan> allTasks, out List<string> cycleDetails)
    {
        cycleDetails = new List<string>();
        var taskMap = allTasks.Where(t => !string.IsNullOrWhiteSpace(t.TaskId))
                              .GroupBy(t => t.TaskId, StringComparer.OrdinalIgnoreCase)
                              .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // 0 = Unvisited, 1 = Visiting, 2 = Visited
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var task in allTasks)
        {
            if (string.IsNullOrWhiteSpace(task.TaskId)) continue;

            if (!state.TryGetValue(task.TaskId, out int s) || s == 0)
            {
                if (Dfs(task.TaskId, taskMap, state, path, cycleDetails))
                {
                    return true;
                }
            }
        }

        return cycleDetails.Count > 0;

        bool Dfs(string currentId, Dictionary<string, TaskPlan> map, Dictionary<string, int> st, List<string> p, List<string> cycles)
        {
            st[currentId] = 1; // Visiting
            p.Add(currentId);

            if (map.TryGetValue(currentId, out var currentTask) && currentTask.Dependencies != null)
            {
                foreach (var depId in currentTask.Dependencies)
                {
                    if (string.IsNullOrWhiteSpace(depId) || !map.ContainsKey(depId) || string.Equals(depId, currentId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Invalid, missing, or self-dependency handled separately
                    }

                    if (!st.TryGetValue(depId, out int depState) || depState == 0)
                    {
                        if (Dfs(depId, map, st, p, cycles)) return true;
                    }
                    else if (depState == 1) // Cycle detected
                    {
                        int startIndex = p.IndexOf(depId);
                        var cyclePath = startIndex >= 0 ? p.Skip(startIndex).ToList() : p.ToList();
                        cyclePath.Add(depId);
                        cycles.Add($"Dependency cycle detected: {string.Join(" -> ", cyclePath)}");
                        return true;
                    }
                }
            }

            st[currentId] = 2; // Visited
            p.RemoveAt(p.Count - 1);
            return false;
        }
    }

    private static bool HasPhaseDependencyCycle(List<PhasePlan> allPhases, out List<string> cycleDetails)
    {
        cycleDetails = new List<string>();
        var phaseMap = allPhases.Where(p => !string.IsNullOrWhiteSpace(p.PhaseId))
                                .GroupBy(p => p.PhaseId, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // 0 = Unvisited, 1 = Visiting, 2 = Visited
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var phase in allPhases)
        {
            if (string.IsNullOrWhiteSpace(phase.PhaseId)) continue;

            if (!state.TryGetValue(phase.PhaseId, out int s) || s == 0)
            {
                if (Dfs(phase.PhaseId, phaseMap, state, path, cycleDetails))
                {
                    return true;
                }
            }
        }

        return cycleDetails.Count > 0;

        bool Dfs(string currentId, Dictionary<string, PhasePlan> map, Dictionary<string, int> st, List<string> p, List<string> cycles)
        {
            st[currentId] = 1; // Visiting
            p.Add(currentId);

            if (map.TryGetValue(currentId, out var currentPhase) && currentPhase.Dependencies != null)
            {
                foreach (var depId in currentPhase.Dependencies)
                {
                    if (string.IsNullOrWhiteSpace(depId) || !map.ContainsKey(depId) || string.Equals(depId, currentId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Invalid, missing, or self-dependency handled separately
                    }

                    if (!st.TryGetValue(depId, out int depState) || depState == 0)
                    {
                        if (Dfs(depId, map, st, p, cycles)) return true;
                    }
                    else if (depState == 1) // Cycle detected
                    {
                        int startIndex = p.IndexOf(depId);
                        var cyclePath = startIndex >= 0 ? p.Skip(startIndex).ToList() : p.ToList();
                        cyclePath.Add(depId);
                        cycles.Add($"Phase dependency cycle detected: {string.Join(" -> ", cyclePath)}");
                        return true;
                    }
                }
            }

            st[currentId] = 2; // Visited
            p.RemoveAt(p.Count - 1);
            return false;
        }
    }
}
