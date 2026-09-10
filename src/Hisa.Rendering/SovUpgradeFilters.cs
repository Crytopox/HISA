using Hisa.Core.Models;

namespace Hisa.Rendering;

public static class SovUpgradeFilters
{
    public static readonly HashSet<string> SingleLevelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Advanced Logistics Network",
        "Cynosural Navigation",
        "Cynosural Suppression",
        "Electric Stability Generator",
        "Exotic Stability Generator",
        "Gamma Stability Generator",
        "Plasma Stability Generator",
        "Supercapital Construction Facilities"
    };

    public const string ExplorationDetectorName = "Exploration Detector";

    public static bool IsSingleLevel(string upgradeName) => SingleLevelNames.Contains(upgradeName);

    public static string BuildFilterKey(SovUpgradeEntry upgrade)
    {
        return IsSingleLevel(upgrade.UpgradeName)
            ? upgrade.UpgradeName
            : $"{upgrade.UpgradeName}|{Math.Clamp(upgrade.Tier, 1, 3)}";
    }

    public static IEnumerable<SovUpgradeEntry> GetVisible(
        IReadOnlyList<SovUpgradeEntry> upgrades,
        IEnumerable<string>? selectedKeys)
    {
        if (selectedKeys is null)
        {
            return upgrades;
        }

        var set = selectedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0)
        {
            return [];
        }

        return upgrades.Where(x => set.Contains(BuildFilterKey(x)));
    }

    public static string BuildSignature(IEnumerable<string>? selectedKeys)
    {
        if (selectedKeys is null)
        {
            return "*";
        }

        var keys = selectedKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return keys.Length == 0 ? "-" : string.Join('\u001f', keys);
    }
}
