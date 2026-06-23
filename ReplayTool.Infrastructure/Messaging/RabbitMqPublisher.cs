using MassTransit;
using ReplayTool.Application.Interfaces;
using ReplayTool.Domain.Events;

namespace ReplayTool.Infrastructure.Messaging;

public class RabbitMqPublisher : IMessagePublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public RabbitMqPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public Task PublishRoutingEventAsync(OrdersRoutingEventV2 evt, CancellationToken ct = default)
        => _publishEndpoint.Publish(evt, ct);

    public Task PublishAssignmentEventAsync(AssignmentSolutionV2Event evt, CancellationToken ct = default)
        => _publishEndpoint.Publish(evt, ct);
}
