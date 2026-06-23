using ReplayTool.Application.Timeline;
using ReplayTool.Application.UseCases;
using ReplayTool.Domain.Events;

namespace ReplayTool.Tests;

public class TimelineTests
{
    private readonly BuildTimelineUseCase _sut = new();

    [Fact]
    public void EmptyInputs_ReturnsEmptyTimeline()
    {
        var result = _sut.Execute(null, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void OnlyRoutingEvents_OrderedByTimestamp()
    {
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddSeconds(5);

        var result = _sut.Execute([MakeRouting(t2), MakeRouting(t1)], null);

        result.Should().HaveCount(2);
        result[0].Timestamp.Should().Be(t1);
        result[1].Timestamp.Should().Be(t2);
        result.Should().AllSatisfy(e => e.EventType.Should().Be(ReplayEventType.RoutingResponse));
    }

    [Fact]
    public void OnlyAssignmentEvents_OrderedByTimestamp()
    {
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddSeconds(3);

        var result = _sut.Execute(null, [MakeAssignment(t2), MakeAssignment(t1)]);

        result.Should().HaveCount(2);
        result[0].Timestamp.Should().Be(t1);
        result[1].Timestamp.Should().Be(t2);
        result.Should().AllSatisfy(e => e.EventType.Should().Be(ReplayEventType.AssignmentSolution));
    }

    [Fact]
    public void MixedEvents_MergedAndOrderedByTimestamp()
    {
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddSeconds(3);
        var t3 = t1.AddSeconds(7);

        var routing = new List<OrdersRoutingEventV2> { MakeRouting(t1), MakeRouting(t3) };
        var assignment = new List<AssignmentSolutionV2Event> { MakeAssignment(t2) };

        var result = _sut.Execute(routing, assignment);

        result.Should().HaveCount(3);
        result[0].Timestamp.Should().Be(t1);
        result[0].EventType.Should().Be(ReplayEventType.RoutingResponse);
        result[1].Timestamp.Should().Be(t2);
        result[1].EventType.Should().Be(ReplayEventType.AssignmentSolution);
        result[2].Timestamp.Should().Be(t3);
        result[2].EventType.Should().Be(ReplayEventType.RoutingResponse);
    }

    [Fact]
    public void EqualTimestamps_AssignmentSolutionBeforeRoutingResponse()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var result = _sut.Execute([MakeRouting(t)], [MakeAssignment(t)]);

        result.Should().HaveCount(2);
        result[0].EventType.Should().Be(ReplayEventType.AssignmentSolution);
        result[1].EventType.Should().Be(ReplayEventType.RoutingResponse);
    }

    [Fact]
    public void FirstEvent_HasOffsetZero()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = _sut.Execute([MakeRouting(t)], null);

        result[0].OffsetMs.Should().Be(0);
    }

    [Fact]
    public void OffsetMs_IsMillisecondsDeltaFromFirstEvent()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMilliseconds(1500);
        var t2 = t0.AddMilliseconds(4200);

        var routing = new List<OrdersRoutingEventV2> { MakeRouting(t0), MakeRouting(t2) };
        var assignment = new List<AssignmentSolutionV2Event> { MakeAssignment(t1) };

        var result = _sut.Execute(routing, assignment);

        result[0].OffsetMs.Should().Be(0);
        result[1].OffsetMs.Should().Be(1500);
        result[2].OffsetMs.Should().Be(4200);
    }

    [Fact]
    public void RoutingEvent_PayloadPreservedInRoutingEventProperty()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var evt = MakeRouting(t);

        var result = _sut.Execute([evt], null);

        result[0].RoutingEvent.Should().BeSameAs(evt);
        result[0].AssignmentEvent.Should().BeNull();
    }

    [Fact]
    public void AssignmentEvent_PayloadPreservedInAssignmentEventProperty()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var evt = MakeAssignment(t);

        var result = _sut.Execute(null, [evt]);

        result[0].AssignmentEvent.Should().BeSameAs(evt);
        result[0].RoutingEvent.Should().BeNull();
    }

    private static OrdersRoutingEventV2 MakeRouting(DateTime timestamp) => new()
    {
        AreaId = "area-1",
        TenantId = "tenant-1",
        CorrelationId = Guid.NewGuid().ToString(),
        Timestamp = timestamp,
        RobotRoutes = []
    };

    private static AssignmentSolutionV2Event MakeAssignment(DateTime timestamp) => new()
    {
        TenantId = "tenant-1",
        AreaId = "area-1",
        Timestamp = timestamp,
        Decisions = []
    };
}
