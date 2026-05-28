namespace Hisa.Core.Models;

public sealed class MapLayoutRegionSummary
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public int? SourceRegionId { get; init; }
    public bool IsGameRegion { get; init; }
    public bool IsReadOnly { get; init; }

    public override string ToString() => Name;
}
