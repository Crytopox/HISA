using Avalonia;
using Avalonia.Media;
using Hisa.Core.Models;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Rendering.Controls;

public sealed partial class MapControl
{
    public static readonly StyledProperty<HistoricalMapSnapshot?> HistoricalActivityProperty =
        AvaloniaProperty.Register<MapControl, HistoricalMapSnapshot?>(nameof(HistoricalActivity));
    public HistoricalMapSnapshot? HistoricalActivity
    {
        get => GetValue(HistoricalActivityProperty);
        set => SetValue(HistoricalActivityProperty, value);
    }
    private static readonly Color[] HistoricalColors = BuildHistoricalColors();

    private static Color[] BuildHistoricalColors()
    {
        var colors = new Color[34];
        colors[0] = Color.Parse("#617B9C"); // No claim of inactivity under uncertain coverage/unknown value.
        colors[1] = Color.Parse("#454A53"); // Observed zero.
        for (var i = 2; i < colors.Length; i++)
        {
            var t = (i - 2) / 31d;
            colors[i] = t < 0.5 ? BlendColors(Color.Parse("#E5D76D"), Color.Parse("#F17D43"), t * 2)
                : BlendColors(Color.Parse("#F17D43"), Color.Parse("#F05BCE"), (t - 0.5) * 2);
        }
        return colors;
    }
    private Color GetDisplayedNodeColor(MapNode node)
        => HistoricalActivity is { } history && ViewMode != MapViewMode.UniverseRegions
            ? HistoricalColors[history.LevelFor(node.Id)] : GetNodeBaseColor(node, NodeColorMode);
}
