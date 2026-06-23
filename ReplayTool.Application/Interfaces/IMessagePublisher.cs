using ReplayTool.Domain.Events;

namespace ReplayTool.Application.Interfaces;

public interface IMessagePublisher
{
    Task PublishRoutingEventAsync(OrdersRoutingEventV2 evt, CancellationToken ct = default);
    Task PublishAssignmentEventAsync(AssignmentSolutionV2Event evt, CancellationToken ct = default);
}
