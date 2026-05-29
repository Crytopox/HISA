namespace Hisa.App;

public sealed class LyCoverageCandidateRow
{
    public required long CenterSystemId { get; init; }
    public required string CenterSystemName { get; init; }
    public required string RegionName { get; init; }
    public required int CoveredCount { get; init; }
    public required int TargetCount { get; init; }
    public string CoveredText => $"{CoveredCount}/{TargetCount}";
    public required double CoveragePercent { get; init; }
    public string CoveragePercentText => $"{CoveragePercent:0.0}";
    public required double AverageDistanceLy { get; init; }
    public string AverageDistanceLyText => $"{AverageDistanceLy:0.00}";
    public required double MaxDistanceLy { get; init; }
    public string MaxDistanceLyText => $"{MaxDistanceLy:0.00}";
    public required IReadOnlyList<string> UncoveredSystems { get; init; }
    public required IReadOnlyList<long> CoveredSystemIds { get; init; }
    public required IReadOnlyList<long> UncoveredSystemIds { get; init; }
    public string UncoveredPreview =>
        UncoveredSystems.Count == 0
            ? "-"
            : string.Join(", ", UncoveredSystems.Take(12)) + (UncoveredSystems.Count > 12 ? " ..." : string.Empty);
}

public sealed class LyCoverageAnalysisResult
{
    public required IReadOnlyList<LyCoverageCandidateRow> Candidates { get; init; }
    public required IReadOnlyList<string> InvalidTokens { get; init; }
    public required int TargetCount { get; init; }
    public required int CandidateCountTested { get; init; }
}
