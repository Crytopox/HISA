namespace Hisa.App.ViewModels;

public sealed class AlertPopupCard
{
    public required string Title { get; init; }
    public required string Details { get; init; }
    public required string TimestampLabel { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
