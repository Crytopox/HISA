namespace Hisa.App;

public sealed class JumpRouteCandidateRow
{
    public required string RouteText { get; init; }
    public required IReadOnlyList<long> RouteSystemIds { get; init; }
    public required IReadOnlyList<string> RouteSystemNames { get; init; }
    public required int VisitedCount { get; init; }
    public required int TargetCount { get; init; }
    public string VisitedText => $"{VisitedCount}/{TargetCount}";
    public required double TotalDistanceLy { get; init; }
    public string TotalDistanceLyText => $"{TotalDistanceLy:0.00}";
    public required double MaxLegLy { get; init; }
    public string MaxLegLyText => $"{MaxLegLy:0.00}";
    public required IReadOnlyList<string> SkippedSystems { get; init; }
    public required IReadOnlyList<string> SkippedReasonLines { get; init; }
    public required IReadOnlyList<long> SkippedSystemIds { get; init; }
    public required IReadOnlyList<JumpRouteLegRow> Legs { get; init; }
    public string LegsText => Legs.Count == 0
        ? "-"
        : string.Join(Environment.NewLine, Legs.Select((l, i) => $"{i + 1}. {l.From} -> {l.To} ({l.DistanceLy:0.00} LY)"));
    public string SkippedPreview =>
        SkippedSystems.Count == 0
            ? "-"
            : string.Join(", ", SkippedSystems.Take(10)) + (SkippedSystems.Count > 10 ? " ..." : string.Empty);
    public string SkippedReasonPreview =>
        SkippedReasonLines.Count == 0
            ? "-"
            : string.Join(" | ", SkippedReasonLines.Take(2)) + (SkippedReasonLines.Count > 2 ? " ..." : string.Empty);
}

public sealed class JumpRouteLegRow
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required double DistanceLy { get; init; }
}

public sealed class JumpRouteAnalysisResult
{
    public required IReadOnlyList<JumpRouteCandidateRow> Candidates { get; init; }
    public required IReadOnlyList<string> InvalidTokens { get; init; }
    public required int TargetCount { get; init; }
    public string? OrderingMessage { get; init; }
    public bool OrderingFailed { get; init; }
}
