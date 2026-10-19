using Hisa.Core.Models.Intelligence;

namespace Hisa.Core;

/// <summary>Deterministic temporal evidence only. No inference of gate, grid, fleet presence or pilot behavior.</summary>
public static class CombatDetector
{
    public const string RuleVersion = "temporal-v1";
    public static DateTimeOffset Partition(DateTimeOffset time) => new(time.UtcTicks / TimeSpan.TicksPerHour / 6 * (6 * TimeSpan.TicksPerHour), TimeSpan.Zero);

    public static IReadOnlyList<CombatEngagement> Detect(IReadOnlyList<CombatInput> input, long system, DateTimeOffset partition,
        ActivityCoverage coverage, DateTimeOffset now, CombatDetectionOptions options, bool limited = false,
        Func<decimal, decimal, decimal>? addExact = null, CancellationToken cancellationToken = default)
    {
        if (input.Count > 4096) throw new ArgumentOutOfRangeException(nameof(input));
        if (options.InterKillGap <= TimeSpan.Zero || options.InterKillGap > TimeSpan.FromMinutes(30) ||
            options.MaximumEngagementDuration < options.InterKillGap || options.MaximumEngagementDuration > TimeSpan.FromHours(6) ||
            options.SignificantLosses < 2 || options.LargeBattleLosses < options.SignificantLosses || options.CampPvpLosses < 2 ||
            options.CampIntervals < 3 || options.CampDuration < TimeSpan.FromMinutes(30) || options.SignificantKnownIsk < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        var rows = input.Where(r => r.SystemId == system && Partition(r.OccurredUtc) == partition)
            .DistinctBy(r => r.Id).OrderBy(r => r.OccurredUtc).ThenBy(r => r.Id).ToArray();
        var events = new List<CombatEngagement>();
        for (int start = 0; start < rows.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = start + 1;
            while (end < rows.Length && rows[end].OccurredUtc - rows[end - 1].OccurredUtc <= options.InterKillGap &&
                   rows[end].OccurredUtc - rows[start].OccurredUtc <= options.MaximumEngagementDuration) end++;
            var group = rows[start..end];
            if (group.Length >= 2)
            {
                var pvp = group.Count(r => r.ConfirmedPvp);
                var isk = Sum(group);
                var sustained = group[^1].OccurredUtc - group[0].OccurredUtc >= TimeSpan.FromMinutes(30) &&
                    group.Select(r => r.OccurredUtc.UtcTicks / TimeSpan.FromMinutes(15).Ticks).Distinct().Count() >= 3;
                var classification = pvp >= options.LargeBattleLosses || pvp >= options.SignificantLosses && sustained
                    ? EngagementClassification.SustainedBattle
                    : pvp >= options.SignificantLosses || pvp >= 3 && isk >= options.SignificantKnownIsk
                        ? EngagementClassification.SignificantEngagement : EngagementClassification.SmallEngagement;
                var scaleRule = classification switch
                {
                    EngagementClassification.SustainedBattle => $"Battle rule: ≥ {options.LargeBattleLosses} confirmed PvP losses, or ≥ {options.SignificantLosses} across ≥ 30 min and ≥ 3 UTC 15-minute intervals.",
                    EngagementClassification.SignificantEngagement => $"Significant rule: ≥ {options.SignificantLosses} confirmed PvP losses, or ≥ 3 with ≥ {options.SignificantKnownIsk:N0} known ISK.",
                    _ => "Below significant / large battle thresholds; association remains a small engagement."
                };
                events.Add(Create(group, classification, $"{group.Length} recorded losses; {pvp} confirmed PvP. Successive gaps ≤ {options.InterKillGap.TotalMinutes:0} min; duration ≤ {options.MaximumEngagementDuration.TotalMinutes:0} min. {scaleRule} Temporal association does not establish opposing fleets."));
            }
            start = end;
        }
        // An independent persistence pattern: broad spread, repeated PvP, quiet intervals,
        // and no single 15-minute spike dominating the evidence. High volume alone never qualifies.
        var pvpRows = rows.Where(r => r.ConfirmedPvp).ToArray();
        var intervals = pvpRows.GroupBy(r => r.OccurredUtc.UtcTicks / TimeSpan.FromMinutes(15).Ticks).ToArray();
        var quiet = pvpRows.Zip(pvpRows.Skip(1)).Count(pair => pair.Second.OccurredUtc - pair.First.OccurredUtc >= TimeSpan.FromMinutes(5));
        if (pvpRows.Length >= options.CampPvpLosses && intervals.Length >= options.CampIntervals && quiet >= 3 &&
            pvpRows[^1].OccurredUtc - pvpRows[0].OccurredUtc >= options.CampDuration && intervals.Max(g => g.Count()) * 2 <= pvpRows.Length &&
            events.Select(e => e.ConfirmedPvpCount).DefaultIfEmpty(0).Max() * 10 <= pvpRows.Length * 7)
            events.Add(Create(pvpRows, EngagementClassification.PossibleCampActivity,
                $"{pvpRows.Length} confirmed PvP losses across {intervals.Length} UTC 15-minute intervals with {quiet} quiet gaps ≥ 5 min; no interval contains more than half and no engagement contains over 70%. Possible repeated interception; independent fights or traffic can produce this pattern. No gate/grid location or continued fleet presence is established."));
        return events.OrderBy(e => e.FirstUtc).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();

        decimal Sum(IEnumerable<CombatInput> group) => group.Aggregate(0m, (sum, row) => addExact is null ? checked(sum + (row.Value ?? 0)) : addExact(sum, row.Value ?? 0));
        CombatEngagement Create(CombatInput[] group, EngagementClassification kind, string explanation) => new(
            $"{system}:{partition.UtcTicks}:{(kind == EngagementClassification.PossibleCampActivity ? "camp" : "engagement")}:{group[0].Id}", system, group[0].OccurredUtc, group[^1].OccurredUtc,
            group.Length, group.Count(r => r.ConfirmedPvp), Sum(group), group.Count(r => r.Value is null), kind,
            group.GroupBy(r => r.ShipTypeId).Select(g => new CombatLoss(g.Key, g.Count())).OrderByDescending(g => g.Count).ThenBy(g => g.ShipTypeId).Take(8).ToArray(),
            new(RuleVersion, explanation, group.Count(r => r.ConfirmedPvp) >= 2 ? EngagementConfidence.CorroboratedPvpPattern : EngagementConfidence.TemporalPattern, limited),
            coverage, now, partition) { MemberKillmailIds = group.Select(r => r.Id).ToArray() };
    }
}
