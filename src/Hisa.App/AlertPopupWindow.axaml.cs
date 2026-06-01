using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;

namespace Hisa.App;

public partial class AlertPopupWindow : Window
{
    public bool IsDragModeEnabled { get; set; }
    public event EventHandler<PixelPoint>? DragPositionCommitted;

    public AlertPopupWindow()
    {
        InitializeComponent();
        PositionChanged += OnWindowPositionChanged;
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsDragModeEnabled)
        {
            return;
        }

        try
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
        catch
        {
        }
    }

    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsDragModeEnabled)
        {
            return;
        }

        DragPositionCommitted?.Invoke(this, Position);
        e.Handled = true;
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (!IsDragModeEnabled)
        {
            return;
        }

        DragPositionCommitted?.Invoke(this, e.Point);
    }
}
