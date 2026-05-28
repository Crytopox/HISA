namespace Hisa.App;

public sealed class ManualSovUpgradeOption
{
    public required string UpgradeName { get; init; }
    public required int MaxTier { get; init; }
    public override string ToString() => UpgradeName;
}
