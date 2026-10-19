using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Services.Intelligence;

public enum SpoolEntryStatus
{
    Valid,
    Corrupt,
    TransientFailure
}

public enum SpoolQuarantineResult
{
    Moved,
    Retry,
    Abandoned
}

public sealed class SpoolBatch
{
    public bool AccessFailed { get; init; }

    public IReadOnlyList<SpoolEntry> Entries { get; init; } = [];
}

public readonly record struct SpoolEntry(
    long KillmailId,
    KillmailObservation? Observation,
    string Path,
    SpoolEntryStatus Status,
    string? Detail);

public sealed class IntelligenceRecoverySpool
{
    public const int MaxQuarantineAttempts = 3;

    private readonly string _directory;
    private readonly int _maxEntries;
    private readonly object _stateGate = new();
    private readonly Dictionary<string, int> _quarantineFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _abandoned = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private int _pending;
    private int _scanSucceeded;

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

    internal Exception? InjectedAccessException { get; set; }

    public string DirectoryPath => _directory;

    public int PendingCount => Volatile.Read(ref _pending);

    public int ActionableCount
    {
        get
        {
            lock (_stateGate)
            {
                return Math.Max(0, Volatile.Read(ref _pending) - _abandoned.Count);
            }
        }
    }

    public bool ScanSucceeded => Volatile.Read(ref _scanSucceeded) == 1;

    public bool TryRefresh()
    {
        if (InjectedAccessException is not null)
        {
            Volatile.Write(ref _scanSucceeded, 0);
            return false;
        }

        try
        {
            if (!Directory.Exists(_directory))
            {
                Volatile.Write(ref _pending, 0);
                Volatile.Write(ref _scanSucceeded, 1);
                return true;
            }

            var count = Directory.EnumerateFiles(_directory, "*.json").Take(_maxEntries + 1).Count();
            Volatile.Write(ref _pending, count);
            Volatile.Write(ref _scanSucceeded, 1);
            return true;
        }
        catch (Exception ex) when (IsFilesystem(ex))
        {
            Volatile.Write(ref _scanSucceeded, 0);
            return false;
        }
    }

    public bool TryAppend(KillmailObservation observation)
    {
        try
        {
            if (InjectedAccessException is not null)
            {
                return false;
            }

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

    public SpoolBatch ReadBatch(int max)
    {
        if (InjectedAccessException is not null)
        {
            return new SpoolBatch { AccessFailed = true };
        }

        if (max < 1 || !Directory.Exists(_directory))
        {
            return new SpoolBatch();
        }

        var entries = new List<SpoolEntry>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_directory, "*.json").Take(max + _abandoned.Count).ToArray();
        }
        catch (Exception ex) when (IsFilesystem(ex))
        {
            return new SpoolBatch { AccessFailed = true };
        }

        foreach (var path in files)
        {
            if (entries.Count >= max)
            {
                break;
            }

            lock (_stateGate)
            {
                if (_abandoned.Contains(path))
                {
                    continue;
                }
            }

            entries.Add(ReadEntry(path));
        }

        return new SpoolBatch { Entries = entries };
    }

    public SpoolQuarantineResult TryQuarantine(string path)
    {
        lock (_stateGate)
        {
            if (_abandoned.Contains(path))
            {
                return SpoolQuarantineResult.Abandoned;
            }
        }

        try
        {
            if (InjectedAccessException is not null)
            {
                throw InjectedAccessException;
            }

            if (!File.Exists(path))
            {
                DecrementPending();
                return SpoolQuarantineResult.Moved;
            }

            var quarantine = Path.Combine(_directory, "quarantine");
            Directory.CreateDirectory(quarantine);
            var destination = Path.Combine(quarantine, Path.GetFileName(path));
            if (File.Exists(destination))
            {
                destination += "." + Guid.NewGuid().ToString("N");
            }

            File.Move(path, destination);
            DecrementPending();
            return SpoolQuarantineResult.Moved;
        }
        catch (Exception ex) when (IsFilesystem(ex))
        {
            lock (_stateGate)
            {
                var attempts = _quarantineFailures.GetValueOrDefault(path) + 1;
                _quarantineFailures[path] = attempts;
                if (attempts >= MaxQuarantineAttempts)
                {
                    _abandoned.Add(path);
                    return SpoolQuarantineResult.Abandoned;
                }
            }

            return SpoolQuarantineResult.Retry;
        }
    }

    public void Acknowledge(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                DecrementPending();
            }
        }
        catch (Exception ex) when (IsFilesystem(ex))
        {
            // The entry stays pending and replay remains idempotent.
        }
    }

    private SpoolEntry ReadEntry(string path)
    {
        if (!long.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var killmailId))
        {
            return new SpoolEntry(0, null, path, SpoolEntryStatus.Corrupt, "Spool file name is not a killmail id.");
        }

        try
        {
            var document = JsonSerializer.Deserialize<SpoolDocument>(File.ReadAllText(path), _json);
            KillmailObservation? observation = null;
            string? error = "Spool record is structurally invalid.";
            if (document is not null && document.TryToObservation(out observation, out error))
            {
                return new SpoolEntry(killmailId, observation, path, SpoolEntryStatus.Valid, null);
            }

            return new SpoolEntry(killmailId, null, path, SpoolEntryStatus.Corrupt, error);
        }
        catch (JsonException)
        {
            return new SpoolEntry(killmailId, null, path, SpoolEntryStatus.Corrupt, "Spool record is not valid JSON.");
        }
        catch (Exception ex) when (IsFilesystem(ex))
        {
            return new SpoolEntry(killmailId, null, path, SpoolEntryStatus.TransientFailure, ex.Message);
        }
    }

    private void DecrementPending()
    {
        var updated = Interlocked.Decrement(ref _pending);
        if (updated < 0)
        {
            Volatile.Write(ref _pending, 0);
        }
    }

    private string EntryPath(long killmailId)
        => Path.Combine(_directory, killmailId.ToString(CultureInfo.InvariantCulture) + ".json");

    private static bool IsFilesystem(Exception exception)
        => exception is IOException or UnauthorizedAccessException;

    private sealed class SpoolDocument
    {
        public long KillmailId { get; set; }
        public string? Source { get; set; }
        public long? SourceSequence { get; set; }
        public long SolarSystemId { get; set; }
        public DateTimeOffset OccurredAtUtc { get; set; }
        public DateTimeOffset ReceivedAtUtc { get; set; }
        public decimal? TotalValue { get; set; }
        public string? Hash { get; set; }
        public SpoolVictim? Victim { get; set; }
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

        public bool TryToObservation(out KillmailObservation? observation, out string? error)
        {
            observation = null;
            if (KillmailId <= 0)
            {
                error = "KillmailId is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Source))
            {
                error = "Source is missing.";
                return false;
            }

            if (SolarSystemId <= 0)
            {
                error = "SolarSystemId is missing.";
                return false;
            }

            if (OccurredAtUtc == default || ReceivedAtUtc == default)
            {
                error = "Occurrence timestamp is missing.";
                return false;
            }

            if (Victim is null || Victim.ShipTypeId <= 0)
            {
                error = "Victim ship is missing.";
                return false;
            }

            observation = new KillmailObservation
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
            error = null;
            return true;
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
