using Avalonia.Media.Imaging;

namespace Hisa.App;

public sealed class SovUpgradeListRow
{
    public required Bitmap? Icon { get; init; }
    public required string SystemName { get; init; }
    public required string UpgradeName { get; init; }
    public required string TierText { get; init; }
}
