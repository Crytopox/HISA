namespace Hisa.Core.Models;

public enum RoutingCostMode
{
    HopCount = 0,
    Euclidean = 1
}

public sealed class RoutingRequest
{
    public required MapGraph Graph { get; init; }
    public required IReadOnlyCollection<long> SourceSystemIds { get; init; }
    public required long TargetSystemId { get; init; }
    public RoutingCostMode CostMode { get; init; } = RoutingCostMode.HopCount;
    public bool IncludeAnsiblexLinks { get; init; }
    public IReadOnlyList<AnsiblexLinkEntry> AnsiblexLinks { get; init; } = [];
    public double AnsiblexCostMultiplier { get; init; } = 1.0;
}

public sealed class RoutingDistancesRequest
{
    public required MapGraph Graph { get; init; }
    public required IReadOnlyCollection<long> SourceSystemIds { get; init; }
    public RoutingCostMode CostMode { get; init; } = RoutingCostMode.HopCount;
    public bool IncludeAnsiblexLinks { get; init; }
    public IReadOnlyList<AnsiblexLinkEntry> AnsiblexLinks { get; init; } = [];
    public double AnsiblexCostMultiplier { get; init; } = 1.0;
}

public sealed class RoutingPathResult
{
    public bool Found { get; init; }
    public double TotalCost { get; init; }
    public int HopCount { get; init; }
    public IReadOnlyList<long> NodeIds { get; init; } = [];
}
