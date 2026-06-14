using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Hisa.App;

public partial class IntelSettingsWindow : Window
{
    private static readonly IBrush DefaultFeedbackBrush = Brush.Parse("#8A96A8");
    private static readonly IBrush SuccessFeedbackBrush = Brush.Parse("#7CD1A0");
    private static readonly IBrush ErrorFeedbackBrush = Brush.Parse("#F3C0C0");
    private int _saveFeedbackVersion;

    public IntelSettingsWindow()
    {
        InitializeComponent();
    }

    public IntelSettingsWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }

    private async void OnSaveIntelSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        await vm.SaveIntelSettingsAsync();
        var wasApplied = string.Equals(vm.StatusText, "Intel settings saved and applied.", StringComparison.Ordinal);
        if (wasApplied)
        {
            await ShowTemporarySaveFeedbackAsync("Applied.", SuccessFeedbackBrush, TimeSpan.FromSeconds(3));
            return;
        }

        SaveFeedbackText.Text = vm.StatusText;
        SaveFeedbackText.Foreground = ErrorFeedbackBrush;
    }

    private async void OnClearIntelHistoryClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        await vm.ClearIntelAndKillmailHistoryAsync();
    }

    private async Task ShowTemporarySaveFeedbackAsync(string text, IBrush brush, TimeSpan duration)
    {
        var version = ++_saveFeedbackVersion;
        SaveFeedbackText.Text = text;
        SaveFeedbackText.Foreground = brush;
        await Task.Delay(duration);
        if (version != _saveFeedbackVersion)
        {
            return;
        }

        SaveFeedbackText.Text = "Changes apply immediately.";
        SaveFeedbackText.Foreground = DefaultFeedbackBrush;
    }
}
