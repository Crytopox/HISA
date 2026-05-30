using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia;
using Avalonia.VisualTree;
using System.Linq;

namespace Hisa.App;

public partial class CharactersWindow : Window
{
    private int? _lastDragSourceCharacterId;
    private int? _lastDragTargetCharacterId;
    private readonly DispatcherTimer _dragAutoScrollTimer;
    private int _dragAutoScrollDirection;
    private Border? _activeDragCardBorder;
    private IBrush? _activeDragCardOriginalBorderBrush;
    private Thickness _activeDragCardOriginalBorderThickness;
    private const double DragAutoScrollEdgeThreshold = 36;
    private const double DragAutoScrollStep = 10;

    public CharactersWindow()
    {
        InitializeComponent();
        AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnAnyPointerWheelChanged,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        _dragAutoScrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(35), DispatcherPriority.Input, (_, _) =>
        {
            if (_dragAutoScrollDirection == 0)
            {
                return;
            }

            var nextY = Math.Max(
                0,
                Math.Min(
                    CharacterCardsScrollViewer.Extent.Height,
                    CharacterCardsScrollViewer.Offset.Y + (_dragAutoScrollDirection * DragAutoScrollStep)));
            CharacterCardsScrollViewer.Offset = new Vector(CharacterCardsScrollViewer.Offset.X, nextY);
        });
    }

    public CharactersWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }

    private async void OnDragEnabledCharacterPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not Control control ||
            control.Tag is not int characterId)
        {
            return;
        }

        control.Opacity = 0.65;
        if (control.DataContext is MainWindowViewModel.CharacterTrackingCardViewModel card)
        {
            card.IsDragging = true;
        }
        ApplyDragHighlight(characterId);
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText($"hisa-character-id:{characterId}"));
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            control.Opacity = 1.0;
            if (control.DataContext is MainWindowViewModel.CharacterTrackingCardViewModel sourceCard)
            {
                sourceCard.IsDragging = false;
            }
            ClearDragHighlight();
            _dragAutoScrollDirection = 0;
            _dragAutoScrollTimer.Stop();
            _lastDragSourceCharacterId = null;
            _lastDragTargetCharacterId = null;
        }

        // Keep compiler happy for vm capture and future use.
        _ = vm;
    }

    private void OnCharactersScrollDragOver(object? sender, DragEventArgs e)
    {
        UpdateAutoScroll(e.GetPosition(CharacterCardsScrollViewer));
    }

    private void OnCharactersScrollDragLeave(object? sender, RoutedEventArgs e)
    {
        _dragAutoScrollDirection = 0;
        _dragAutoScrollTimer.Stop();
    }

    private void OnCharactersScrollDrop(object? sender, DragEventArgs e)
    {
        _dragAutoScrollDirection = 0;
        _dragAutoScrollTimer.Stop();
    }

    private void OnMoveCharacterUpClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not Button button ||
            button.DataContext is not MainWindowViewModel.CharacterTrackingCardViewModel card)
        {
            return;
        }

        vm.MoveCharacterTrackingUpAmongEnabled(card.CharacterId);
    }

    private void OnMoveCharacterDownClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not Button button ||
            button.DataContext is not MainWindowViewModel.CharacterTrackingCardViewModel card)
        {
            return;
        }

        vm.MoveCharacterTrackingDownAmongEnabled(card.CharacterId);
    }

    private void OnEnabledCharacterCardDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not Border border ||
            border.DataContext is not MainWindowViewModel.CharacterTrackingCardViewModel targetCard)
        {
            return;
        }

        var dragText = e.DataTransfer.TryGetText();
        if (string.IsNullOrWhiteSpace(dragText) ||
            !dragText.StartsWith("hisa-character-id:", StringComparison.Ordinal) ||
            !int.TryParse(dragText["hisa-character-id:".Length..], out var sourceCharacterId))
        {
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
        UpdateAutoScroll(e.GetPosition(CharacterCardsScrollViewer));

        if (_lastDragSourceCharacterId == sourceCharacterId &&
            _lastDragTargetCharacterId == targetCard.CharacterId)
        {
            return;
        }

        _lastDragSourceCharacterId = sourceCharacterId;
        _lastDragTargetCharacterId = targetCard.CharacterId;
        vm.MoveCharacterTrackingAmongEnabled(sourceCharacterId, targetCard.CharacterId);
        ApplyDragHighlight(sourceCharacterId);
    }

    private void OnEnabledCharacterCardDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not Border border ||
            border.DataContext is not MainWindowViewModel.CharacterTrackingCardViewModel targetCard)
        {
            return;
        }

        var dragText = e.DataTransfer.TryGetText();
        if (string.IsNullOrWhiteSpace(dragText) ||
            !dragText.StartsWith("hisa-character-id:", StringComparison.Ordinal) ||
            !int.TryParse(dragText["hisa-character-id:".Length..], out var sourceCharacterId))
        {
            return;
        }

        _lastDragSourceCharacterId = null;
        _lastDragTargetCharacterId = null;
        vm.MoveCharacterTrackingAmongEnabled(sourceCharacterId, targetCard.CharacterId);
    }

    private void OnCharacterEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not CheckBox checkBox ||
            checkBox.DataContext is not MainWindowViewModel.CharacterTrackingCardViewModel card)
        {
            return;
        }

        vm.SetCharacterTrackingEnabled(card.CharacterId, checkBox.IsChecked == true);
    }

    private void UpdateAutoScroll(Point pointInScrollViewer)
    {
        var h = CharacterCardsScrollViewer.Bounds.Height;
        if (pointInScrollViewer.Y <= DragAutoScrollEdgeThreshold)
        {
            _dragAutoScrollDirection = -1;
            if (!_dragAutoScrollTimer.IsEnabled)
            {
                _dragAutoScrollTimer.Start();
            }
            return;
        }

        if (pointInScrollViewer.Y >= h - DragAutoScrollEdgeThreshold)
        {
            _dragAutoScrollDirection = 1;
            if (!_dragAutoScrollTimer.IsEnabled)
            {
                _dragAutoScrollTimer.Start();
            }
            return;
        }

        _dragAutoScrollDirection = 0;
        _dragAutoScrollTimer.Stop();
    }

    private void OnAnyPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_lastDragSourceCharacterId is null)
        {
            return;
        }

        var delta = e.Delta.Y;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        var direction = delta > 0 ? -1.0 : 1.0;
        var nextY = Math.Max(
            0,
            Math.Min(
                CharacterCardsScrollViewer.Extent.Height,
                CharacterCardsScrollViewer.Offset.Y + (direction * 42)));
        CharacterCardsScrollViewer.Offset = new Vector(CharacterCardsScrollViewer.Offset.X, nextY);
        e.Handled = true;
    }

    private void ApplyDragHighlight(int characterId)
    {
        ClearDragHighlight();
        var cardBorder = CharacterCardsScrollViewer.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(x =>
                x.DataContext is MainWindowViewModel.CharacterTrackingCardViewModel card &&
                card.CharacterId == characterId);
        if (cardBorder is null)
        {
            return;
        }

        _activeDragCardBorder = cardBorder;
        _activeDragCardOriginalBorderBrush = cardBorder.BorderBrush;
        _activeDragCardOriginalBorderThickness = cardBorder.BorderThickness;
        cardBorder.BorderBrush = new SolidColorBrush(Color.Parse("#A9D6FF"));
        cardBorder.BorderThickness = new Thickness(2);
    }

    private void ClearDragHighlight()
    {
        if (_activeDragCardBorder is null)
        {
            return;
        }

        _activeDragCardBorder.BorderBrush = _activeDragCardOriginalBorderBrush;
        _activeDragCardBorder.BorderThickness = _activeDragCardOriginalBorderThickness;
        _activeDragCardBorder = null;
    }
}
