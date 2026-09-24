using System.Collections.Generic;

namespace AIBridge.Models;

public class PlanChangeSummary
{
    public List<string> AddedPhases { get; set; } = new();
    public List<string> RemovedPhases { get; set; } = new();
    public List<string> ModifiedPhases { get; set; } = new();
    public List<string> AddedTasks { get; set; } = new();
    public List<string> RemovedTasks { get; set; } = new();
    public List<string> ModifiedTasks { get; set; } = new();
    public List<string> DependencyChanges { get; set; } = new();
}
