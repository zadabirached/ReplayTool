namespace ReplayTool.Application;

public static class CaseFileType
{
    public const string CustomerOrder = "JobService-CustomerOrder.Topic";
    public const string RoutingResponses = "routingResponses-v2";
    public const string AssignmentSolution = "anytask-solution-v2";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        CustomerOrder,
        RoutingResponses,
        AssignmentSolution,
    };

    public static bool IsAllowed(string type) => All.Contains(type);

    public static string Filename(string type) => $"{type}.json";
}
