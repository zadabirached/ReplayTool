using ReplayTool.Application.Timeline;
using ReplayTool.Domain.Events;

namespace ReplayTool.Application.UseCases;

public class BuildTimelineUseCase
{
    public IReadOnlyList<TimelineEntry> Execute(
        IReadOnlyList<OrdersRoutingEventV2>? routingEvents,
        IReadOnlyList<AssignmentSolutionV2Event>? assignmentEvents)
    {
        var raw = new List<(DateTime Timestamp, ReplayEventType Type, object Payload)>();

        foreach (var e in routingEvents ?? [])
            raw.Add((e.Timestamp, ReplayEventType.RoutingResponse, e));

        foreach (var e in assignmentEvents ?? [])
            raw.Add((e.Timestamp, ReplayEventType.AssignmentSolution, e));

        // Ascending by Timestamp; equal timestamps: AssignmentSolution (0) before RoutingResponse (1)
        raw.Sort((a, b) =>
        {
            var cmp = DateTime.Compare(a.Timestamp, b.Timestamp);
            return cmp != 0 ? cmp : ((int)a.Type).CompareTo((int)b.Type);
        });

        if (raw.Count == 0) return [];

        var t0 = raw[0].Timestamp;
        var result = new List<TimelineEntry>(raw.Count);

        foreach (var (ts, type, payload) in raw)
        {
            var offsetMs = (long)(ts - t0).TotalMilliseconds;
            result.Add(type == ReplayEventType.RoutingResponse
                ? new TimelineEntry(type, ts, offsetMs, (OrdersRoutingEventV2)payload, null)
                : new TimelineEntry(type, ts, offsetMs, null, (AssignmentSolutionV2Event)payload));
        }

        return result;
    }
}
