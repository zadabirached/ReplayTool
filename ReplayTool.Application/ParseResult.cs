namespace ReplayTool.Application;

public record ParseError(int Index, string Reason);

public record FileParseResult<T>
{
    public IReadOnlyList<T> Events { get; init; } = [];
    public IReadOnlyList<ParseError> Errors { get; init; } = [];
}

public record CaseParseResult(
    FileParseResult<Domain.Events.CustomerOrderEvent>? CustomerOrders,
    FileParseResult<Domain.Events.OrdersRoutingEventV2>? RoutingResponses,
    FileParseResult<Domain.Events.AssignmentSolutionV2Event>? AssignmentSolutions
);
