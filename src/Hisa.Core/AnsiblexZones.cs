using Hisa.Core.Models;

namespace Hisa.Core;

public readonly record struct AnsiblexShipClass(string Name, double BaseCostTj)
{
    public override string ToString() => $"{Name} ({BaseCostTj:0.##} TJ)";
}

public static class AnsiblexZones
{
    public const double MetersPerLightYear = 9_460_000_000_000_000.0;
    public const double CapacityTj = 1250;
    public const double AverageRechargeTjPerHour = 200;
    public const double FullRechargeHours = 6.25;
    public const string DefaultShipClassName = "Strategic Cruiser";

    private static readonly AnsiblexShipClass[] Classes =
    [
        new("Capsule", 1),
        new("Exhumer", 1),
        new("Expedition Frigate", 1),
        new("Freighter", 1),
        new("Hauler", 1),
        new("Jump Freighter", 1),
        new("Mining Barge", 1),
        new("Prototype Exploration Ship", 1),
        new("Shuttle", 1),
        new("Special Edition Yachts", 1),
        new("Corvette", 1),
        new("Frigate", 4),
        new("Covert Ops", 5),
        new("Destroyer", 6),
        new("Assault Frigate", 7),
        new("Blockade Runner", 7),
        new("Deep Space Transport", 7),
        new("Interceptor", 8),
        new("Interdictor", 8.5),
        new("Command Destroyer", 9),
        new("Electronic Attack Ship", 9),
        new("Logistics Frigate", 9),
        new("Tactical Destroyer", 9),
        new("Industrial Command Ship", 10),
        new("Cruiser", 10),
        new("Heavy Interdiction Cruiser", 10.5),
        new("Stealth Bomber", 11),
        new("Force Recon Ship", 11.5),
        new("Heavy Assault Cruiser", 11.5),
        new("Combat Recon Ship", 11.5),
        new("Flag Cruiser", 12),
        new("Logistics", 12.5),
        new("Strategic Cruiser", 13),
        new("Attack Battlecruiser", 14),
        new("Combat Battlecruiser", 14.5),
        new("Expedition Command Ship", 15),
        new("Command Ship", 16.5),
        new("Battleship", 16.5),
        new("Black Ops", 18),
        new("Marauder", 19),
        new("Rorqual", 19)
    ];

    public static IReadOnlyList<AnsiblexShipClass> ShipClasses => Classes;

    public static int ZoneFromDistanceLy(double distanceLy)
    {
        if (distanceLy <= 5)
        {
            return 1;
        }

        if (distanceLy <= 10)
        {
            return 2;
        }

        if (distanceLy <= 15)
        {
            return 3;
        }

        if (distanceLy <= 20)
        {
            return 4;
        }

        return 5;
    }

    public static int MultiplierForZone(int zone) => zone switch
    {
        <= 1 => 0,
        2 => 2,
        3 => 6,
        4 => 9,
        _ => 15
    };

    public static double CostTj(double baseCostTj, int destinationZone)
    {
        if (destinationZone <= 1)
        {
            return 0;
        }

        return baseCostTj * MultiplierForZone(destinationZone);
    }

    public static int JumpsFromFullCapacitor(double costTj)
    {
        if (costTj <= 0)
        {
            return 0;
        }

        return (int)Math.Floor(CapacityTj / costTj);
    }

    public static double DistanceLy(double fromX, double fromY, double fromZ, double toX, double toY, double toZ)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;
        var dz = toZ - fromZ;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / MetersPerLightYear;
    }

    public static AnsiblexSystemZone ZoneFor(double capitalX, double capitalY, double capitalZ, double x, double y, double z)
    {
        var distance = DistanceLy(capitalX, capitalY, capitalZ, x, y, z);
        return new AnsiblexSystemZone
        {
            Zone = ZoneFromDistanceLy(distance),
            DistanceLy = distance
        };
    }

    public static string ColorHex(int zone) => zone switch
    {
        1 => "#3DDC97",
        2 => "#4C9AFF",
        3 => "#F5C06A",
        4 => "#F08A3C",
        _ => "#E85D5D"
    };

    public static string FormatZone(AnsiblexSystemZone zone) =>
        $"Z{zone.Zone} · {zone.DistanceLy:0.00} LY";

    public static string FormatCostTj(double costTj) =>
        costTj <= 0 ? "free" : $"{costTj:0.##} TJ";

    public static bool TryGetShipClass(string? name, out AnsiblexShipClass shipClass)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var candidate in Classes)
            {
                if (string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    shipClass = candidate;
                    return true;
                }
            }
        }

        shipClass = default;
        return false;
    }

    public static AnsiblexShipClass GetShipClassOrDefault(string? name) =>
        TryGetShipClass(name, out var shipClass)
            ? shipClass
            : Classes.First(x => x.Name == DefaultShipClassName);

    public static string BuildPlanningSummary(string? capitalName, AnsiblexShipClass ship, int? highestZone)
    {
        var capacity = $"Capacity {CapacityTj:0} TJ · ~{AverageRechargeTjPerHour:0} TJ/hour · full recharge ~{FullRechargeHours:0.##} h.";
        if (string.IsNullOrWhiteSpace(capitalName))
        {
            return $"Set a capital system to calculate zones. {capacity}";
        }

        if (highestZone is null)
        {
            return $"{capitalName}. Endpoint distances are unavailable. {capacity}";
        }

        if (highestZone.Value <= 1)
        {
            return $"{capitalName}. {ship.Name} ({ship.BaseCostTj:0.##} TJ): jumps into zone 1 are free. {capacity}";
        }

        var cost = CostTj(ship.BaseCostTj, highestZone.Value);
        var jumps = JumpsFromFullCapacitor(cost);
        return $"{capitalName}. {capacity} {ship.Name} ({ship.BaseCostTj:0.##} TJ): {jumps} jumps from a full bridge into zone {highestZone.Value} ({cost:0.##} TJ).";
    }
}
