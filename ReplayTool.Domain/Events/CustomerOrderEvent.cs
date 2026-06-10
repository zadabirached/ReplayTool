namespace ReplayTool.Domain.Events;

// Represents a CustomerOrder as captured from the JobService-CustomerOrder.Topic event sink.
// JobId carries the business UUID (Job.JobId); Job contains full job info when present.
public record CustomerOrderEvent
{
    public required string OrderId { get; set; }
    public Guid JobId { get; set; }
    public Guid? MissionId { get; set; }
    public string? MissionName { get; set; }
    public string? Status { get; set; }
    public int ProgressionRate { get; set; }
    public string? OrderType { get; set; }
    public int Sequence { get; set; }
    public bool IsTemplate { get; set; }
    public bool IsCompleted { get; set; }
    public Guid? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceRegistryName { get; set; }
    public string? DeviceType { get; set; }
    public string? AssignedBy { get; set; }
    public string? AssignmentType { get; set; }
    public DateTime? AssignedOn { get; set; }
    public DateTime? QueuedAt { get; set; }
    public int ReassignmentCount { get; set; }
    public string? ReassignedDeviceName { get; set; }
    public string? ReassignedDeviceRegistryName { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime? EstimatedEndTime { get; set; }
    public double? EstimatedRemainingDistance { get; set; }
    public DateTime? UpdatedByOrderTrackingOn { get; set; }
    public string? CancellationReason { get; set; }
    public string? CancellationDescription { get; set; }
    public string? Errors { get; set; }
    public string? OperatingModes { get; set; }
    public string? BundleId { get; set; }
    public string? RobotOrderId { get; set; }
    public string[]? PreviousRobotOrderIds { get; set; }
    public Guid TenantId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedOn { get; set; }

    // When present, carries full Job details so the Job row can be upserted (W3-T5).
    public JobData? Job { get; set; }
}

public record JobData
{
    public Guid JobId { get; set; }
    public Guid? AutomationId { get; set; }
    public string? AutomationName { get; set; }
    public int? AutomationChainingId { get; set; }
    public string? AreaId { get; set; }
    public Guid? FleetId { get; set; }
    public Guid TenantId { get; set; }
    public string? JobType { get; set; }
    public int JobPriority { get; set; }
    public string[]? Tags { get; set; }
    public bool IsCompleted { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedOn { get; set; }
}
