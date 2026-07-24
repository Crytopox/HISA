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
    public bool HasIntelCard => IntelCard is not null;
    public bool HasZkillmailCard => ZkillmailCard is not null;
    public bool HasOverlayCard => HasIntelCard || HasZkillmailCard;
    public bool HasNoOverlayCard => !HasOverlayCard;
    public bool HasGenericAlert => HasNoOverlayCard && !IsMiningSiteAlert;
    public required DateTime ExpiresAtUtc { get; init; }
}
