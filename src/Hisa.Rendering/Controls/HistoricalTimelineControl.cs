using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Rendering.Controls;

/// <summary>At most 96 bars, prepared on result change. No timers, elements per bucket, or database access.</summary>
public sealed class HistoricalTimelineControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<HistoricalTimelineBin>?> BinsProperty =
        AvaloniaProperty.Register<HistoricalTimelineControl, IReadOnlyList<HistoricalTimelineBin>?>(nameof(Bins));
    public IReadOnlyList<HistoricalTimelineBin>? Bins { get => GetValue(BinsProperty); set => SetValue(BinsProperty, value); }
    private static readonly IBrush Bar = new ImmutableSolidColorBrush(Color.Parse("#E8AD67"));
    private static readonly IBrush Baseline = new ImmutableSolidColorBrush(Color.Parse("#617B9C"));
    static HistoricalTimelineControl() => AffectsRender<HistoricalTimelineControl>(BinsProperty);
    public override void Render(DrawingContext context)
    {
        if (Bins is not { Count: > 0 } bins) return;
        var count = Math.Min(96, bins.Count);
        var width = Bounds.Width / count;
        for (var index = 0; index < count; index++)
        {
            var height = Math.Max(1, Math.Clamp(bins[index].Height, 0, 1) * (Bounds.Height - 2));
            context.FillRectangle(bins[index].Killmails > 0 ? Bar : Baseline,
                new Rect(index * width, Bounds.Height - height, Math.Max(0.5, width - 1), height));
        }
    }
}
