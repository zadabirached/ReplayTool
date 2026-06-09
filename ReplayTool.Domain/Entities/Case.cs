namespace ReplayTool.Domain.Entities;

public record Case
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public CaseStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
}

public enum CaseStatus
{
    Draft,
    Ready,
    Running,
    Completed,
    Failed
}
