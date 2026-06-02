using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Hisa.App;

public partial class AboutWindow : Window
{
    private const string DiscordUrl = "https://discord.gg/ByVCvC6UY9";
    private const string GitHubUrl = "https://github.com/Crytopox/HISA";
    private const int AuthorCharacterId = 96469091;
    private static readonly HttpClient PortraitHttpClient = new();
    private bool _authorPortraitLoaded;

    public AboutWindow()
    {
        InitializeComponent();
        VersionTextBlock.Text = $"Version {GetApplicationVersion()}";
        Opened += async (_, _) => await LoadAuthorPortraitAsync();
    }

    private static string GetApplicationVersion()
    {
        var assembly = typeof(AboutWindow).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+')[0];
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    private void OnOpenGitHubClicked(object? sender, RoutedEventArgs e)
        => OpenUrl(GitHubUrl);

    private void OnOpenDiscordClicked(object? sender, RoutedEventArgs e)
        => OpenUrl(DiscordUrl);

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
        => Close();

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
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // External links are best-effort only.
        }
    }
}
