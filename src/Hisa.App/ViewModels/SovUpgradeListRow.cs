using Avalonia.Media.Imaging;

namespace Hisa.App;

public sealed class SovUpgradeListRow
{
    public required string SystemName { get; init; }
    public required IReadOnlyList<SovSystemUpgradeChip> Upgrades { get; init; }
}

public sealed class SovSystemUpgradeChip
{
    public required Bitmap? Icon { get; init; }
    public required string Label { get; init; }
}

public sealed class SovUpgradeCountRow
{
    public required Bitmap? Icon { get; init; }
    public required string DisplayName { get; init; }
    public required int Count { get; init; }
}

public sealed class SovUpgradeCountGroup
{
    public required string Title { get; init; }
    public required int Count { get; init; }
    public required IReadOnlyList<SovUpgradeCountRow> Upgrades { get; init; }
}
