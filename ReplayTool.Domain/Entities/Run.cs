namespace ReplayTool.Domain.Entities;

public enum RunStatus { Pending, Running, Completed, Failed }
public enum ReplayMode { Normal, Fast }
public enum RunStepResult { Inserted, Skipped, Failed, Published }

public record RunStep(
    string Phase,
    string EventId,
    RunStepResult Result,
    long? OffsetMs = null,
    long? ScheduledOffsetMs = null,
    long? ActualOffsetMs = null,
    string? Error = null);

public record Run
{
    public Guid Id { get; init; }
    public RunStatus Status { get; init; }
    public bool DryRun { get; init; }
    public double SpeedFactor { get; init; } = 1.0;
    public ReplayMode Mode { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public IReadOnlyList<RunStep> Steps { get; init; } = [];
}
