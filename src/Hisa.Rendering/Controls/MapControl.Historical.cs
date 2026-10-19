using Avalonia;
using Avalonia.Media;
using System.Collections.Frozen;
using Hisa.Core.Models;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Rendering.Controls;

public sealed partial class MapControl
{
    public static readonly StyledProperty<CombatMapSnapshot?> CombatAnalysisProperty =
        AvaloniaProperty.Register<MapControl, CombatMapSnapshot?>(nameof(CombatAnalysis));
    public CombatMapSnapshot? CombatAnalysis { get => GetValue(CombatAnalysisProperty); set => SetValue(CombatAnalysisProperty, value); }
    private static readonly Pen CombatSmallPen = new(new SolidColorBrush(Color.Parse("#79BFC8")), 1.2);
    private static readonly Pen CombatBattlePen = new(new SolidColorBrush(Color.Parse("#B891DC")), 1.6);
    private static readonly Pen CombatCampPen = new(new SolidColorBrush(Color.Parse("#C7A56C")), 1.2);
    private void DrawCombatMarker(DrawingContext context, MapNode node, Point point, double radius)
    {
        if (ViewMode == MapViewMode.UniverseRegions || CombatAnalysis is not { } analysis ||
            !analysis.Markers.TryGetValue(node.Id, out var marker)) return;
        var size = Math.Clamp(radius + 3, 4, 10);
        if ((marker & 3) > 0) context.DrawEllipse(null, (marker & 3) == 2 ? CombatBattlePen : CombatSmallPen, point, size, size);
        if ((marker & 4) != 0) context.DrawRectangle(null, CombatCampPen, new Rect(point.X-size, point.Y-size, size*2, size*2));
    }
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
