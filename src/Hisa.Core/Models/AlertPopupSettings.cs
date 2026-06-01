namespace Hisa.Core.Models;

public enum AlertPopupAnchor
{
    TopRight = 0,
    TopLeft = 1,
    BottomRight = 2,
    BottomLeft = 3
}

public sealed class AlertPopupSettings
{
    public bool Enabled { get; init; } = true;
    public int MaxCards { get; init; } = 8;
    public int AutoDismissSeconds { get; init; } = 18;
    public double Opacity { get; init; } = 0.95;
    public int Width { get; init; } = 250;
    public int Height { get; init; } = 320;
    public AlertPopupAnchor Anchor { get; init; } = AlertPopupAnchor.TopRight;
    public int OffsetX { get; init; } = 12;
    public int OffsetY { get; init; } = 56;
}
