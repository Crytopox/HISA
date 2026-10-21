using Avalonia;

namespace Hisa.App;

public sealed record IntelHistoryScreen(string? Name, PixelRect WorkingArea, double Scaling, bool IsPrimary);

public static class IntelHistoryPlacement
{
    public static WindowPlacementState Restore(WindowPlacementState? saved, IReadOnlyList<IntelHistoryScreen> screens,
        double defaultWidth = 380, double defaultHeight = 520)
    {
        if (screens.Count == 0) screens = [new(null, new(0, 0, 1920, 1080), 1, true)];
        var named = screens.Where(s => !string.IsNullOrEmpty(saved?.ScreenDisplayName) && s.Name == saved.ScreenDisplayName).ToList();
        // Two monitors can expose the same model/display name. Use saved geometry in that case.
        var screen = saved is null ? null : (named.Count == 1 ? named[0] : null)
            ?? screens.FirstOrDefault(s => saved.ScreenWorkingAreaX == s.WorkingArea.X && saved.ScreenWorkingAreaY == s.WorkingArea.Y &&
                saved.ScreenWorkingAreaWidth == s.WorkingArea.Width && saved.ScreenWorkingAreaHeight == s.WorkingArea.Height);
        // Older saves only have desktop coordinates. Keep those on the matching monitor.
        if (screen is null && saved is not null && string.IsNullOrEmpty(saved.ScreenDisplayName) && saved.ScreenWorkingAreaX is null)
            screen = screens.FirstOrDefault(s => s.WorkingArea.Contains(new PixelPoint(saved.PositionX, saved.PositionY)));
        screen ??= screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
        var area = screen.WorkingArea;
        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        var width = Math.Clamp(saved?.Width ?? defaultWidth, 280, Math.Max(280, area.Width / scale));
        var height = Math.Clamp(saved?.Height ?? defaultHeight, 160, Math.Max(160, area.Height / scale));
        var savedScale = saved?.ScreenScaling is > 0 ? saved.ScreenScaling.Value : scale;
        var x = saved?.ScreenOffsetX is int offsetX ? area.X + (int)Math.Round(offsetX / savedScale * scale) : saved?.PositionX ?? area.X + 24;
        var y = saved?.ScreenOffsetY is int offsetY ? area.Y + (int)Math.Round(offsetY / savedScale * scale) : saved?.PositionY ?? area.Y + 64;
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(width * scale)));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(height * scale)));
        return new()
        {
            Width = width, Height = height, PositionX = x, PositionY = y, WindowState = "Normal",
            ScreenDisplayName = screen.Name, ScreenScaling = scale,
            ScreenWorkingAreaX = area.X, ScreenWorkingAreaY = area.Y,
            ScreenWorkingAreaWidth = area.Width, ScreenWorkingAreaHeight = area.Height,
            ScreenOffsetX = x - area.X, ScreenOffsetY = y - area.Y
        };
    }
}
