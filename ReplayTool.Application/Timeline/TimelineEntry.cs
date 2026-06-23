using ReplayTool.Domain.Events;

namespace ReplayTool.Application.Timeline;

// Enum ordinal used as tie-break: AssignmentSolution (0) sorts before RoutingResponse (1).
public enum ReplayEventType { AssignmentSolution, RoutingResponse }

public record TimelineEntry(
    ReplayEventType EventType,
    DateTime Timestamp,
    long OffsetMs,
    OrdersRoutingEventV2? RoutingEvent,
    AssignmentSolutionV2Event? AssignmentEvent);
