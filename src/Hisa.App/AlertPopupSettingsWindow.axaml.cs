using Avalonia.Controls;
using Avalonia.Interactivity;
using Hisa.Core.Models;

namespace Hisa.App;

public partial class AlertPopupSettingsWindow : Window
{
    private readonly MainWindowViewModel? _vm;
    private bool _placementModeActive;
    public event EventHandler<bool>? PlacementModeChanged;

    public AlertPopupSettingsWindow()
    {
        InitializeComponent();
        PopupAnchorComboBox.ItemsSource = Enum.GetValues<AlertPopupAnchor>();
    }

    public AlertPopupSettingsWindow(MainWindowViewModel vm) : this()
    {
        _vm = vm;
        LoadFromViewModel();
    }

    private void LoadFromViewModel()
    {
        if (_vm is null)
        {
            return;
        }

        var popup = _vm.GetAlertPopupSettingsSnapshot();
        PopupEnabledCheckBox.IsChecked = popup.Enabled;
        PopupClickThroughCheckBox.IsChecked = popup.ClickThrough;
        PopupMaxCardsTextBox.Text = popup.MaxCards.ToString();
        PopupAutoDismissTextBox.Text = popup.AutoDismissSeconds.ToString();
        PopupOpacitySlider.Value = Math.Clamp(popup.Opacity * 100.0, 20.0, 100.0);
        PopupAnchorComboBox.SelectedItem = popup.Anchor;
        PopupOffsetXTextBox.Text = popup.OffsetX.ToString();
        PopupOffsetYTextBox.Text = popup.OffsetY.ToString();
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var maxCards = int.TryParse(PopupMaxCardsTextBox.Text, out var mc) ? mc : 8;
        var dismiss = int.TryParse(PopupAutoDismissTextBox.Text, out var ds) ? ds : 18;
        var opacity = Math.Clamp(PopupOpacitySlider.Value / 100.0, 0.2, 1.0);
        var offsetX = int.TryParse(PopupOffsetXTextBox.Text, out var ox) ? ox : 12;
        var offsetY = int.TryParse(PopupOffsetYTextBox.Text, out var oy) ? oy : 56;
        var anchor = PopupAnchorComboBox.SelectedItem is AlertPopupAnchor a ? a : AlertPopupAnchor.TopRight;

        await _vm.SaveAlertPopupSettingsAsync(new AlertPopupSettings
        {
            Enabled = PopupEnabledCheckBox.IsChecked == true,
            ClickThrough = PopupClickThroughCheckBox.IsChecked == true,
            MaxCards = maxCards,
            AutoDismissSeconds = dismiss,
            Opacity = opacity,
            Anchor = anchor,
            OffsetX = offsetX,
            OffsetY = offsetY
        });
    }

    private void OnPlacementToggleClicked(object? sender, RoutedEventArgs e)
    {
        _placementModeActive = !_placementModeActive;
        PlacementModeChanged?.Invoke(this, _placementModeActive);
        ApplyPlacementModeUiState();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    public void SetPlacementModeState(bool active)
    {
        _placementModeActive = active;
        ApplyPlacementModeUiState();
    }

    public void UpdateOffsetsFromPlacement(int offsetX, int offsetY)
    {
        PopupOffsetXTextBox.Text = offsetX.ToString();
        PopupOffsetYTextBox.Text = offsetY.ToString();
    }

    private void ApplyPlacementModeUiState()
    {
        PlacementToggleButton.Content = _placementModeActive ? "Stop Placement" : "Start Placement";
        PlacementStatusTextBlock.Text = _placementModeActive ? "Placement mode: On" : "Placement mode: Off";
    }
}
