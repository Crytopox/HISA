using Avalonia.Media.Imaging;

namespace Hisa.App.ViewModels;

public sealed class AlertPopupCard
{
    public required string TimestampLabel { get; init; }
    public required string Title { get; init; }
    public required string Details { get; init; }
    public IntelOverlayCard? IntelCard { get; init; }
    public ZkillmailOverlayCard? ZkillmailCard { get; init; }
    public bool IsMiningSiteAlert { get; init; }
    public string MiningSiteSystemName { get; init; } = string.Empty;
    public string MiningSiteUpgradeLabel { get; init; } = string.Empty;
    public Bitmap? MiningSiteIcon { get; init; }
    public bool MiningSiteWasOverdue { get; init; }
    public string MiningSiteAccentHex => MiningSiteWasOverdue ? "#F2C94C" : "#51D88A";
    public string MiningSiteAccentBackgroundHex => MiningSiteWasOverdue ? "#3A3218" : "#183A2B";
    public string MiningSiteLabel => MiningSiteWasOverdue ? "MINING SITE OVERDUE" : "MINING SITE READY";
    public bool HasIntelCard => IntelCard is not null;
    public bool HasZkillmailCard => ZkillmailCard is not null;
    public bool HasOverlayCard => HasIntelCard || HasZkillmailCard;
    public bool HasNoOverlayCard => !HasOverlayCard;
    public EnvironmentalAlertPopupCard? EnvironmentalCard { get; init; }
    public bool HasEnvironmentalCard => EnvironmentalCard is not null;
    public bool HasGenericAlert => HasNoOverlayCard && !IsMiningSiteAlert && !HasEnvironmentalCard;
    public int? JumpCount { get; init; }
    public bool HasJumpCount => JumpCount is not null;
    public string JumpCountLabel => JumpCount is int jumps ? $"{jumps}J" : string.Empty;
    public string JumpCountTooltip => JumpCount is int jumps
        ? jumps == 1 ? "1 jump" : $"{jumps} jumps"
        : string.Empty;
    public required DateTime ExpiresAtUtc { get; init; }
}

public sealed class EnvironmentalAlertPopupCard
{
    public required long SolarSystemId { get; init; }
    public required string SystemName { get; init; }
    public required string ConstellationName { get; init; }
    public required string RegionName { get; init; }
    public required string CategoryLabel { get; init; }
    public required string Headline { get; init; }
    public required string AccentHex { get; init; }
    public required string AccentBackgroundHex { get; init; }
    public required string DetailOne { get; init; }
    public required string DetailTwo { get; init; }
    public required string DetailThree { get; init; }
    public required string TimestampLabel { get; init; }
}
