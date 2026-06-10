namespace Hisa.App;

public sealed class WindowPlacementState
{
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required int PositionX { get; init; }
    public required int PositionY { get; init; }
    public required string WindowState { get; init; }
    public int? ScreenWorkingAreaX { get; init; }
    public int? ScreenWorkingAreaY { get; init; }
    public int? ScreenWorkingAreaWidth { get; init; }
    public int? ScreenWorkingAreaHeight { get; init; }
    public int? ScreenOffsetX { get; init; }
    public int? ScreenOffsetY { get; init; }
    public int? MainWindowOffsetX { get; init; }
    public int? MainWindowOffsetY { get; init; }
}
