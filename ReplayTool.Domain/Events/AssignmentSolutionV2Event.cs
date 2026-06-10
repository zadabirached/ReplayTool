namespace ReplayTool.Domain.Events;

public record AssignmentSolutionV2Event
{
    public required string TenantId { get; set; }
    public required string AreaId { get; set; }
    public required DateTime Timestamp { get; set; }
    public required List<Decision> Decisions { get; set; }
}

public record Decision
{
    public required string RobotRegistryName { get; set; }
    public required List<Goal> ExecutionSequence { get; set; }
}

public record Goal
{
    public required string OrderId { get; set; }
    public required string GoalId { get; set; }
    public required string PoiId { get; set; }
    public List<UsedCompartment> UsedCompartments { get; set; } = [];
    public List<ResultingLoad> ResultingLoads { get; set; } = [];
    public DateTime EstimatedTimeOfCompletion { get; set; }
}

public record UsedCompartment
{
    public required string ActionId { get; set; }
    public required string CompartmentId { get; set; }
}

public record ResultingLoad
{
    public required string CompartmentId { get; set; }
    public required string LoadId { get; set; }
}
