namespace ReplayTool.Domain.Events;

public record OrdersRoutingEventV2
{
    public required string AreaId { get; set; }
    public required string TenantId { get; set; }
    public required string CorrelationId { get; set; }
    public required DateTime Timestamp { get; set; }
    public required Dictionary<string, Route> RobotRoutes { get; set; } = new();

    public record Route
    {
        public required string OrderId { get; set; }
        public required int OrderUpdateId { get; set; }
        public required Node LastNode { get; set; }
        public required int LastNodeSequenceId { get; set; }
        public required List<PathSegment> Base { get; set; }
        public required List<PathSegment> Horizon { get; set; }
    }

    public record PathSegment
    {
        public required Edge Edge { get; set; }
        public required Node Target { get; set; }
    }

    public record Edge
    {
        public required string Id { get; set; }
        public Trajectory? Trajectory { get; set; }
        public double? MaxSpeed { get; set; }
        public double? MaxHeight { get; set; }
        public double? MinHeight { get; set; }
        public double? Orientation { get; set; }
        public string? Direction { get; set; }
        public bool? RotationAllowed { get; set; }
        public double? MaxRotationSpeed { get; set; }
        public double? Length { get; set; }
        public Dictionary<string, object>? AdditionalParameters { get; set; }
        public Corridor? Corridor { get; set; }
    }

    public record Node
    {
        public required string Id { get; set; }
        public required NodePosition Position { get; set; }
        public double? AllowedDeviationXY { get; set; }
        public double? AllowedDeviationTheta { get; set; }
        public Dictionary<string, object>? AdditionalParameters { get; set; }
    }

    public record NodePosition
    {
        public required double X { get; set; }
        public required double Y { get; set; }
        public double? Theta { get; set; }
    }

    public record Position
    {
        public required double X { get; set; }
        public required double Y { get; set; }
    }

    public record Trajectory
    {
        public List<ControlPoint>? ControlPoints { get; set; }
        public required int Degree { get; set; }
        public required List<double> KnotVector { get; set; }
    }

    public record ControlPoint
    {
        public required double Weight { get; set; }
        public required Position Position { get; set; }
    }

    public record Corridor
    {
        public required double LeftWidth { get; set; }
        public required double RightWidth { get; set; }
        public string? CorridorRefPoint { get; set; }
    }
}
