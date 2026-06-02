using Avalonia.Controls;
using Avalonia.Interactivity;
using Hisa.App.Services;

namespace Hisa.App;

public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow()
    {
        InitializeComponent();
    }

    public UpdateAvailableWindow(GitHubUpdateResult update) : this()
    {
        VersionTextBlock.Text = $"Installed: v{update.CurrentVersion.ToString(3)}    Latest: {update.LatestTag}";
    }

    private void OnLaterClicked(object? sender, RoutedEventArgs e)
        => Close();

    private void OnOpenReleasesClicked(object? sender, RoutedEventArgs e)
    {
        ExternalUrlLauncher.Open(GitHubUpdateService.ReleasesUrl);
        Close();
    }
}
