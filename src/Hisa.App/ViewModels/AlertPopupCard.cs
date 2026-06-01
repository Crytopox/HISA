namespace Hisa.App.ViewModels;

public sealed class AlertPopupCard
{
    public required string TimestampLabel { get; init; }
    public required string Title { get; init; }
    public required string Details { get; init; }
    public IntelOverlayCard? IntelCard { get; init; }
    public ZkillmailOverlayCard? ZkillmailCard { get; init; }
    public bool HasIntelCard => IntelCard is not null;
    public bool HasZkillmailCard => ZkillmailCard is not null;
    public bool HasOverlayCard => HasIntelCard || HasZkillmailCard;
    public bool HasNoOverlayCard => !HasOverlayCard;
    public required DateTime ExpiresAtUtc { get; init; }
}
