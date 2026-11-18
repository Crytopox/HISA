using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hisa.App;

public partial class XmppFeedCardView : UserControl
{
    public static readonly StyledProperty<bool> ShowFeedActionsProperty =
        AvaloniaProperty.Register<XmppFeedCardView, bool>(nameof(ShowFeedActions), defaultValue: true);

    public XmppFeedCardView()
    {
        InitializeComponent();
        UpdateFeedActionsVisibility();
    }

    public bool ShowFeedActions
    {
        get => GetValue(ShowFeedActionsProperty);
        set => SetValue(ShowFeedActionsProperty, value);
    }

    public event EventHandler<XmppLinkActivatedEventArgs>? LinkActivated;
    public event EventHandler<RoutedEventArgs>? OpenMessageClick;
    public event EventHandler<RoutedEventArgs>? CopyRawClick;
    public event EventHandler<RoutedEventArgs>? OpenMumbleClick;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowFeedActionsProperty || change.Property == DataContextProperty)
            UpdateFeedActionsVisibility();
    }

    private void UpdateFeedActionsVisibility()
    {
        if (FeedActionsPanel is null) return;
        var card = DataContext as XmppFeedCardVm;
        FeedActionsPanel.IsVisible = ShowFeedActions && card is { ShowActions: true };
    }

    private void OnLinkActivated(object? sender, XmppLinkActivatedEventArgs e) =>
        LinkActivated?.Invoke(this, e);

    private void OnOpenMessage(object? sender, RoutedEventArgs e) =>
        OpenMessageClick?.Invoke(sender, e);

    private void OnCopyRaw(object? sender, RoutedEventArgs e) =>
        CopyRawClick?.Invoke(sender, e);

    private void OnOpenMumble(object? sender, RoutedEventArgs e) =>
        OpenMumbleClick?.Invoke(sender, e);
}
