namespace Hisa.App;

public sealed class WindowPlacementState
{
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required int PositionX { get; init; }
    public required int PositionY { get; init; }
    public required string WindowState { get; init; }
}
