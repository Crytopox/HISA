using Hisa.Core.Models;

namespace Hisa.Logs.GameLogs;

public static class GameLogMiningHistoryReader
{
    public static Task<IReadOnlyDictionary<int, MiningSessionSnapshot>> ReadAsync(
        string gameLogsDirectory,
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameLogsDirectory);

        var fullPath = Path.GetFullPath(gameLogsDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"GameLogs directory was not found: {fullPath}");
        }

        var aggregateByCharacterId = new Dictionary<int, AggregateCharacterState>();

        foreach (var filePath in Directory.EnumerateFiles(fullPath, "*.txt", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(filePath);
            if (!GameLogFileName.TryParse(fileName, out var key))
            {
                continue;
            }

            var lastWriteUtc = File.GetLastWriteTimeUtc(filePath);
            if (key.SessionStartedUtc < cutoffUtc && lastWriteUtc < cutoffUtc)
            {
                continue;
            }

            ReadFile(filePath, key, cutoffUtc, aggregateByCharacterId);
        }

        return Task.FromResult<IReadOnlyDictionary<int, MiningSessionSnapshot>>(aggregateByCharacterId
            .Select(kvp => new { kvp.Key, Snapshot = BuildSnapshot(kvp.Value) })
            .Where(x => x.Snapshot is not null)
            .ToDictionary(x => x.Key, x => x.Snapshot!));
    }

    private static void ReadFile(
        string filePath,
        GameLogFileKey key,
        DateTime cutoffUtc,
        Dictionary<int, AggregateCharacterState> aggregateByCharacterId)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        if (!aggregateByCharacterId.TryGetValue(key.CharacterId, out var aggregate))
        {
            aggregate = new AggregateCharacterState
            {
                CharacterId = key.CharacterId,
                SessionStartedUtc = key.SessionStartedUtc,
                FirstActivityUtc = default,
                LastActivityUtc = key.SessionStartedUtc,
                SourceFilePath = filePath
            };
            aggregateByCharacterId[key.CharacterId] = aggregate;
        }
        else if (key.SessionStartedUtc < aggregate.SessionStartedUtc)
        {
            aggregate.SessionStartedUtc = key.SessionStartedUtc;
        }

        string? line;
        var inHeader = false;
        while ((line = reader.ReadLine()) is not null)
        {
            var normalized = line.TrimStart('\uFEFF').Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            if (normalized.StartsWith("------------------------------------------------------------", StringComparison.Ordinal))
            {
                inHeader = !inHeader;
                if (!inHeader)
                {
                    break;
                }

                continue;
            }

            if (!inHeader)
            {
                continue;
            }

            var listener = GameLogMiningParser.TryParseListener(normalized);
            if (!string.IsNullOrWhiteSpace(listener))
            {
                aggregate.CharacterName = listener;
            }
        }

        while ((line = reader.ReadLine()) is not null)
        {
            if (!GameLogMiningParser.TryParseMiningEvent(line, out var miningEvent) ||
                miningEvent.TimestampUtc < cutoffUtc)
            {
                continue;
            }

            aggregate.LastActivityUtc = miningEvent.TimestampUtc > aggregate.LastActivityUtc
                ? miningEvent.TimestampUtc
                : aggregate.LastActivityUtc;
            if (aggregate.FirstActivityUtc == default || miningEvent.TimestampUtc < aggregate.FirstActivityUtc)
            {
                aggregate.FirstActivityUtc = miningEvent.TimestampUtc;
            }

            switch (miningEvent.Kind)
            {
                case MiningLogEventKind.Yield:
                    var yieldOre = GetOrCreateOre(aggregate, miningEvent.OreName);
                    if (miningEvent.IsCriticalBonus)
                    {
                        yieldOre.BonusUnits += miningEvent.Units;
                    }
                    else
                    {
                        yieldOre.MinedUnits += miningEvent.Units;
                    }
                    yieldOre.LastMinedUtc = miningEvent.TimestampUtc;
                    if (aggregate.CurrentEfficiencyPercent is { } yieldEfficiency)
                    {
                        yieldOre.LastKnownEfficiencyPercent = yieldEfficiency;
                    }
                    aggregate.LastOreName = miningEvent.OreName;
                    break;
                case MiningLogEventKind.Residue:
                    var residueOre = GetOrCreateOre(aggregate, aggregate.LastOreName);
                    residueOre.WasteUnits += miningEvent.Units;
                    break;
                case MiningLogEventKind.SiteEfficiencyChanged:
                    aggregate.CurrentEfficiencyPercent = miningEvent.EfficiencyPercent;
                    aggregate.LastOreName = miningEvent.OreName;
                    break;
            }
        }
    }

    private static AggregateOreState GetOrCreateOre(AggregateCharacterState aggregate, string? oreName)
    {
        var key = string.IsNullOrWhiteSpace(oreName) ? "Unknown" : oreName.Trim();
        if (!aggregate.OresByName.TryGetValue(key, out var ore))
        {
            ore = new AggregateOreState { OreName = key };
            aggregate.OresByName[key] = ore;
        }

        return ore;
    }

    private static MiningSessionSnapshot? BuildSnapshot(AggregateCharacterState aggregate)
    {
        var ores = aggregate.OresByName.Values
            .Where(x => x.MinedUnits > 0 || x.BonusUnits > 0)
            .OrderByDescending(x => x.MinedUnits + x.BonusUnits)
            .Select(x => new MiningOreTotals
            {
                OreName = x.OreName,
                MinedUnits = x.MinedUnits,
                BonusUnits = x.BonusUnits,
                WasteUnits = x.WasteUnits,
                LastMinedUtc = x.LastMinedUtc == default ? aggregate.LastActivityUtc : x.LastMinedUtc,
                LastKnownEfficiencyPercent = x.LastKnownEfficiencyPercent
            })
            .ToList();

        if (ores.Count == 0)
        {
            return null;
        }

        return new MiningSessionSnapshot
        {
            CharacterId = aggregate.CharacterId,
            CharacterName = string.IsNullOrWhiteSpace(aggregate.CharacterName) ? $"Character {aggregate.CharacterId}" : aggregate.CharacterName,
            SessionStartedUtc = aggregate.SessionStartedUtc,
            FirstActivityUtc = aggregate.FirstActivityUtc == default ? aggregate.SessionStartedUtc : aggregate.FirstActivityUtc,
            LastActivityUtc = aggregate.LastActivityUtc,
            SourceFilePath = aggregate.SourceFilePath,
            CurrentEfficiencyPercent = aggregate.CurrentEfficiencyPercent,
            Ores = ores
        };
    }

    private sealed class AggregateCharacterState
    {
        public required int CharacterId { get; init; }
        public string CharacterName { get; set; } = string.Empty;
        public required DateTime SessionStartedUtc { get; set; }
        public required DateTime FirstActivityUtc { get; set; }
        public required DateTime LastActivityUtc { get; set; }
        public required string SourceFilePath { get; set; }
        public string? LastOreName { get; set; }
        public int? CurrentEfficiencyPercent { get; set; }
        public Dictionary<string, AggregateOreState> OresByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AggregateOreState
    {
        public string OreName { get; init; } = string.Empty;
        public long MinedUnits { get; set; }
        public long BonusUnits { get; set; }
        public long WasteUnits { get; set; }
        public DateTime LastMinedUtc { get; set; }
        public int? LastKnownEfficiencyPercent { get; set; }
    }
}
