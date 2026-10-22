namespace Hisa.Core.Models;

public enum FleetBoostFamily { Shield, Armour, Info, Skirmish }

public sealed record FleetBoostCharge(string Id, string Name, FleetBoostFamily Family)
{
    public string ShortName => Name.Replace(" Charge", "", StringComparison.Ordinal);
}

public static class FleetBoostCatalog
{
    public static IReadOnlyList<FleetBoostCharge> Charges { get; } = Array.AsReadOnly(new[]
    {
        new FleetBoostCharge("shield-harmonizing", "Shield Harmonizing Charge", FleetBoostFamily.Shield),
        new FleetBoostCharge("active-shielding", "Active Shielding Charge", FleetBoostFamily.Shield),
        new FleetBoostCharge("shield-extension", "Shield Extension Charge", FleetBoostFamily.Shield),
        new FleetBoostCharge("armor-energizing", "Armor Energizing Charge", FleetBoostFamily.Armour),
        new FleetBoostCharge("rapid-repair", "Rapid Repair Charge", FleetBoostFamily.Armour),
        new FleetBoostCharge("armor-reinforcement", "Armor Reinforcement Charge", FleetBoostFamily.Armour),
        new FleetBoostCharge("sensor-optimization", "Sensor Optimization Charge", FleetBoostFamily.Info),
        new FleetBoostCharge("electronic-superiority", "Electronic Superiority Charge", FleetBoostFamily.Info),
        new FleetBoostCharge("electronic-hardening", "Electronic Hardening Charge", FleetBoostFamily.Info),
        new FleetBoostCharge("evasive-maneuvers", "Evasive Maneuvers Charge", FleetBoostFamily.Skirmish),
        new FleetBoostCharge("interdiction-maneuvers", "Interdiction Maneuvers Charge", FleetBoostFamily.Skirmish),
        new FleetBoostCharge("rapid-deployment", "Rapid Deployment Charge", FleetBoostFamily.Skirmish)
    });
}

public sealed record FleetBoostMessage(DateTime TimestampUtc, string Pilot, IReadOnlyList<string> ChargeIds, bool? Mindlink);
public sealed record FleetBoostPilot(string Name, IReadOnlyList<string> ChargeIds, bool Mindlink, DateTime LastReportUtc);
public sealed record FleetBoostSnapshot(string Channel, DateTime? StartedUtc, IReadOnlyList<FleetBoostPilot> Pilots, string Status);

/// <summary>One in-memory session. The service serializes access and owns file boundaries.</summary>
public sealed class FleetBoostSession
{
    private readonly Dictionary<string, FleetBoostPilot> _pilots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    public DateTime StartedUtc { get; }
    public IReadOnlyList<FleetBoostPilot> Pilots => _pilots.Values.Where(p => p.ChargeIds.Count > 0)
        .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public FleetBoostSession(DateTime startedUtc) => StartedUtc = startedUtc;

    private static string Key(FleetBoostMessage message) =>
        $"{message.TimestampUtc.Ticks}|{message.Pilot.ToUpperInvariant()}|{string.Join(',', message.ChargeIds.Order())}|{message.Mindlink}";

    public void RememberExisting(FleetBoostMessage message)
    {
        if (message.TimestampUtc >= StartedUtc.AddSeconds(-1)) _seen.Add(Key(message));
    }

    public bool Apply(FleetBoostMessage message)
    {
        // EVE timestamps have second precision. Byte boundaries handle the start second.
        if (message.TimestampUtc.Ticks / TimeSpan.TicksPerSecond < StartedUtc.Ticks / TimeSpan.TicksPerSecond ||
            (message.ChargeIds.Count == 0 && message.Mindlink is null)) return false;
        if (!_seen.Add(Key(message))) return false;
        if (_lastMessages.TryGetValue(message.Pilot, out var last) && message.TimestampUtc < last) return false;
        _lastMessages[message.Pilot] = message.TimestampUtc;
        _pilots.TryGetValue(message.Pilot, out var previous);
        _pilots[message.Pilot] = new(message.Pilot,
            message.ChargeIds.Count > 0 ? message.ChargeIds.Distinct().ToArray() : previous?.ChargeIds ?? [],
            message.Mindlink ?? previous?.Mindlink ?? false, message.TimestampUtc);
        return true;
    }

    public bool Remove(string pilot) => _pilots.Remove(pilot);
}
