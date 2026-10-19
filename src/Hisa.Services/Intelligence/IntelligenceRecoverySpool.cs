using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Services.Intelligence;

public sealed class IntelligenceRecoverySpool
{
    private readonly string _directory;
    private readonly int _maxEntries;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private int _pending;

    public IntelligenceRecoverySpool(string directory, int maxEntries)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Recovery spool directory is required.", nameof(directory));
        }

        if (maxEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        _directory = directory;
        _maxEntries = maxEntries;
    }

    public string DirectoryPath => _directory;

    public int PendingCount => Volatile.Read(ref _pending);

    public void Scan()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                Volatile.Write(ref _pending, 0);
                return;
            }

            var count = Directory.EnumerateFiles(_directory, "*.json").Take(_maxEntries + 1).Count();
            Volatile.Write(ref _pending, count);
        }
        catch (Exception)
        {
            Volatile.Write(ref _pending, 0);
        }
    }

    public bool TryAppend(KillmailObservation observation)
    {
        try
        {
            var path = EntryPath(observation.KillmailId);
            var replacing = File.Exists(path);
            if (!replacing && Volatile.Read(ref _pending) >= _maxEntries)
            {
                return false;
            }

            Directory.CreateDirectory(_directory);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(SpoolDocument.From(observation), _json));
            File.Move(temporary, path, overwrite: true);
            if (!replacing)
            {
                Interlocked.Increment(ref _pending);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IReadOnlyList<SpoolEntry> ReadBatch(int max)
    {
        if (max < 1 || !Directory.Exists(_directory))
        {
            return [];
        }

        var entries = new List<SpoolEntry>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").Take(max))
        {
            if (!long.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var killmailId))
            {
                entries.Add(new SpoolEntry(0, null, path));
                continue;
            }

            try
            {
                var document = JsonSerializer.Deserialize<SpoolDocument>(File.ReadAllText(path), _json);
                entries.Add(new SpoolEntry(killmailId, document?.ToObservation(), path));
            }
            catch (JsonException)
            {
                entries.Add(new SpoolEntry(killmailId, null, path));
            }
        }

        return entries;
    }

    public void Acknowledge(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Interlocked.Decrement(ref _pending);
            }
        }
        catch (Exception)
        {
            // The entry stays pending and will be retried.
        }
    }

    private string EntryPath(long killmailId)
        => Path.Combine(_directory, killmailId.ToString(CultureInfo.InvariantCulture) + ".json");

    public readonly record struct SpoolEntry(long KillmailId, KillmailObservation? Observation, string Path);

    private sealed class SpoolDocument
    {
        public long KillmailId { get; set; }
        public string Source { get; set; } = "";
        public long? SourceSequence { get; set; }
        public long SolarSystemId { get; set; }
        public DateTimeOffset OccurredAtUtc { get; set; }
        public DateTimeOffset ReceivedAtUtc { get; set; }
        public decimal? TotalValue { get; set; }
        public string? Hash { get; set; }
        public SpoolVictim Victim { get; set; } = new();
        public List<SpoolAttacker> Attackers { get; set; } = [];

        public static SpoolDocument From(KillmailObservation observation)
        {
            return new SpoolDocument
            {
                KillmailId = observation.KillmailId,
                Source = observation.Source,
                SourceSequence = observation.SourceSequence,
                SolarSystemId = observation.SolarSystemId,
                OccurredAtUtc = observation.OccurredAtUtc,
                ReceivedAtUtc = observation.ReceivedAtUtc,
                TotalValue = observation.TotalValue,
                Hash = observation.Hash,
                Victim = new SpoolVictim
                {
                    CharacterId = observation.Victim.CharacterId,
                    CorporationId = observation.Victim.CorporationId,
                    AllianceId = observation.Victim.AllianceId,
                    ShipTypeId = observation.Victim.ShipTypeId,
                    DamageTaken = observation.Victim.DamageTaken
                },
                Attackers = observation.Attackers.Select(attacker => new SpoolAttacker
                {
                    CharacterId = attacker.CharacterId,
                    CorporationId = attacker.CorporationId,
                    AllianceId = attacker.AllianceId,
                    ShipTypeId = attacker.ShipTypeId,
                    WeaponTypeId = attacker.WeaponTypeId,
                    FinalBlow = attacker.FinalBlow,
                    DamageDone = attacker.DamageDone
                }).ToList()
            };
        }

        public KillmailObservation ToObservation()
        {
            return new KillmailObservation
            {
                KillmailId = KillmailId,
                Source = Source,
                SourceSequence = SourceSequence,
                SolarSystemId = SolarSystemId,
                OccurredAtUtc = OccurredAtUtc,
                ReceivedAtUtc = ReceivedAtUtc,
                TotalValue = TotalValue,
                Hash = Hash,
                Victim = new KillmailVictim
                {
                    CharacterId = Victim.CharacterId,
                    CorporationId = Victim.CorporationId,
                    AllianceId = Victim.AllianceId,
                    ShipTypeId = Victim.ShipTypeId,
                    DamageTaken = Victim.DamageTaken
                },
                Attackers = Attackers.Select(attacker => new KillmailAttacker
                {
                    CharacterId = attacker.CharacterId,
                    CorporationId = attacker.CorporationId,
                    AllianceId = attacker.AllianceId,
                    ShipTypeId = attacker.ShipTypeId,
                    WeaponTypeId = attacker.WeaponTypeId,
                    FinalBlow = attacker.FinalBlow,
                    DamageDone = attacker.DamageDone
                }).ToArray()
            };
        }
    }

    private sealed class SpoolVictim
    {
        public int? CharacterId { get; set; }
        public int? CorporationId { get; set; }
        public int? AllianceId { get; set; }
        public int ShipTypeId { get; set; }
        public long? DamageTaken { get; set; }
    }

    private sealed class SpoolAttacker
    {
        public int? CharacterId { get; set; }
        public int? CorporationId { get; set; }
        public int? AllianceId { get; set; }
        public int? ShipTypeId { get; set; }
        public int? WeaponTypeId { get; set; }
        public bool? FinalBlow { get; set; }
        public long? DamageDone { get; set; }
    }
}
