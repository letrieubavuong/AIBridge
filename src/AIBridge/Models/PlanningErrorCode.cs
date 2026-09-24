namespace AIBridge.Models;

public static class PlanningErrorCode
{
    public const string PlanningDisabled = "PLANNING_DISABLED";
    public const string BrainNotReady = "BRAIN_NOT_READY";
    public const string BrainError = "BRAIN_ERROR";
    public const string PlanningTimeout = "PLANNING_TIMEOUT";
    public const string PlanningCancelled = "PLANNING_CANCELLED";
    public const string InvalidPlan = "INVALID_PLAN";
    public const string DuplicateId = "DUPLICATE_ID";
    public const string InvalidDependency = "INVALID_DEPENDENCY";
    public const string DependencyCycle = "DEPENDENCY_CYCLE";
    public const string PersistenceError = "PERSISTENCE_ERROR";
    public const string PlanNotFound = "PLAN_NOT_FOUND";
    public const string PlanAlreadyApproved = "PLAN_ALREADY_APPROVED";
    public const string ApprovalRequired = "APPROVAL_REQUIRED";
    public const string InvalidRevision = "INVALID_REVISION";
}
