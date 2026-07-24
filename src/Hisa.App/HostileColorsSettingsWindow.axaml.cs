using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Hisa.Core.Models;

namespace Hisa.App;

public partial class HostileColorsSettingsWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindowViewModel? _viewModel;
    private Color _lowPickerColor = Color.Parse("#E6D86C");
    private Color _mediumPickerColor = Color.Parse("#EE8639");
    private Color _highPickerColor = Color.Parse("#D90F13");
    private Color _aboveHighPickerColor = Color.Parse("#DD008C");

    public new event PropertyChangedEventHandler? PropertyChanged;

    public Color LowPickerColor { get => _lowPickerColor; set => SetProperty(ref _lowPickerColor, value); }
    public Color MediumPickerColor { get => _mediumPickerColor; set => SetProperty(ref _mediumPickerColor, value); }
    public Color HighPickerColor { get => _highPickerColor; set => SetProperty(ref _highPickerColor, value); }
    public Color AboveHighPickerColor { get => _aboveHighPickerColor; set => SetProperty(ref _aboveHighPickerColor, value); }

    public HostileColorsSettingsWindow()
    {
        DataContext = this;
        InitializeComponent();
    }

    public HostileColorsSettingsWindow(MainWindowViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        Load(viewModel.GetHostileColorSettingsSnapshot());
    }

    private void Load(HostileColorSettings settings)
    {
        LowMaximumTextBox.Text = settings.LowMaxHostiles.ToString();
        MediumMaximumTextBox.Text = settings.MediumMaxHostiles.ToString();
        HighMaximumTextBox.Text = settings.HighMaxHostiles.ToString();
        LowPickerColor = ParseColor(settings.LowColorHex, "#E6D86C");
        MediumPickerColor = ParseColor(settings.MediumColorHex, "#EE8639");
        HighPickerColor = ParseColor(settings.HighColorHex, "#D90F13");
        AboveHighPickerColor = ParseColor(settings.AboveHighColorHex, "#DD008C");
        ValidationTextBlock.Text = string.Empty;
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (!TryBuildSettings(out var settings, out var message))
        {
            ValidationTextBlock.Text = message;
            return;
        }

        await _viewModel.SaveHostileColorSettingsAsync(settings);
        Close();
    }

    private void OnRestoreDefaultsClicked(object? sender, RoutedEventArgs e) => Load(new HostileColorSettings());

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private bool TryBuildSettings(out HostileColorSettings settings, out string message)
    {
        settings = new HostileColorSettings();
        message = string.Empty;
        if (!int.TryParse(LowMaximumTextBox.Text, out var low) || low < 1 ||
            !int.TryParse(MediumMaximumTextBox.Text, out var medium) || medium <= low ||
            !int.TryParse(HighMaximumTextBox.Text, out var high) || high <= medium)
        {
            message = "Maximum counts must be whole numbers: Low < Medium < High.";
            return false;
        }

        settings = new HostileColorSettings
        {
            LowMaxHostiles = low,
            MediumMaxHostiles = medium,
            HighMaxHostiles = high,
            LowColorHex = LowPickerColor.ToString(),
            MediumColorHex = MediumPickerColor.ToString(),
            HighColorHex = HighPickerColor.ToString(),
            AboveHighColorHex = AboveHighPickerColor.ToString()
        };
        return true;
    }

    private static Color ParseColor(string value, string fallback)
    {
        try { return Color.Parse(value); }
        catch (Exception) { return Color.Parse(fallback); }
    }

    private void SetProperty(ref Color field, Color value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
