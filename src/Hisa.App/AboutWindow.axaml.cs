using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Hisa.App.Services;

namespace Hisa.App;

public partial class AboutWindow : Window
{
    private const string DiscordUrl = "https://discord.gg/ByVCvC6UY9";
    private const string GitHubUrl = "https://github.com/Crytopox/HISA";
    private const int AuthorCharacterId = 96469091;
    private static readonly HttpClient PortraitHttpClient = new();
    private readonly GitHubUpdateService _updateService;
    private bool _authorPortraitLoaded;

    public AboutWindow() : this(new GitHubUpdateService())
    {
    }

    public AboutWindow(GitHubUpdateService updateService)
    {
        InitializeComponent();
        _updateService = updateService;
        VersionTextBlock.Text = $"Version {GitHubUpdateService.GetCurrentVersionText()}";
        Opened += async (_, _) =>
        {
            await LoadAuthorPortraitAsync();
            await RefreshUpdateStatusAsync();
        };
    }

    private void OnOpenGitHubClicked(object? sender, RoutedEventArgs e)
        => OpenUrl(GitHubUrl);

    private void OnOpenDiscordClicked(object? sender, RoutedEventArgs e)
        => OpenUrl(DiscordUrl);

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
        => Close();

    private void OnOpenReleasesClicked(object? sender, RoutedEventArgs e)
        => ExternalUrlLauncher.Open(GitHubUpdateService.ReleasesUrl);

    private async Task RefreshUpdateStatusAsync()
    {
        UpdateStatusTextBlock.Text = "Checking for updates...";
        var update = await _updateService.CheckForUpdatesAsync(forceRefresh: true);
        if (update is null)
        {
            UpdateStatusTextBlock.Text = "Unable to check for updates";
            return;
        }

        if (update.IsUpdateAvailable)
        {
            UpdateStatusTextBlock.Text = $"{update.LatestTag} available";
            OpenReleasesButton.IsVisible = true;
            return;
        }

        UpdateStatusTextBlock.Text = "Up to date";
    }

    private async Task LoadAuthorPortraitAsync()
    {
        if (_authorPortraitLoaded)
        {
            return;
        }

        try
        {
            var url = $"https://images.evetech.net/characters/{AuthorCharacterId}/portrait?tenant=tranquility&size=128";
            using var stream = await PortraitHttpClient.GetStreamAsync(url).ConfigureAwait(false);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory).ConfigureAwait(false);
            memory.Position = 0;
            var bitmap = new Bitmap(memory);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AuthorPortraitImage.Source = bitmap;
                _authorPortraitLoaded = true;
            });
        }
        catch
        {
            // Keep the bundled logo when the portrait service is unavailable.
        }
    }

    private static void OpenUrl(string url)
        => ExternalUrlLauncher.Open(url);
}
