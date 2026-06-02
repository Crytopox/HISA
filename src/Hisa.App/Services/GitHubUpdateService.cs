using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Hisa.Core.Abstractions;

namespace Hisa.App.Services;

public sealed record GitHubUpdateResult(
    Version CurrentVersion,
    Version LatestVersion,
    string LatestTag,
    bool IsUpdateAvailable);

public sealed class GitHubUpdateService
{
    public const string ReleasesUrl = "https://github.com/Crytopox/HISA/releases";

    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Crytopox/HISA/releases/latest";
    private const string LastRemindedTagSettingsKey = "Updates.LastRemindedTag";
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly ISettingsService? _settingsService;
    private readonly object _gate = new();
    private Task<GitHubUpdateResult?>? _cachedCheck;

    public GitHubUpdateService(ISettingsService? settingsService = null)
    {
        _settingsService = settingsService;
    }

    public Task<GitHubUpdateResult?> CheckForUpdatesAsync(bool forceRefresh = false)
    {
        lock (_gate)
        {
            if (forceRefresh || _cachedCheck is null)
            {
                _cachedCheck = CheckForUpdatesCoreAsync();
            }

            return _cachedCheck;
        }
    }

    public static string GetCurrentVersionText()
        => GetCurrentVersion().ToString(3);

    public async Task<bool> ShouldShowStartupReminderAsync(GitHubUpdateResult update)
    {
        if (!update.IsUpdateAvailable || _settingsService is null)
        {
            return update.IsUpdateAvailable;
        }

        try
        {
            var lastRemindedTag = await _settingsService.GetAsync<string>(LastRemindedTagSettingsKey);
            if (string.Equals(lastRemindedTag, update.LatestTag, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            await _settingsService.SetAsync(LastRemindedTagSettingsKey, update.LatestTag);
            return true;
        }
        catch
        {
            return true;
        }
    }

    private static async Task<GitHubUpdateResult?> CheckForUpdatesCoreAsync()
    {
        try
        {
            using var response = await HttpClient.GetAsync(LatestReleaseApiUrl).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            if (!json.RootElement.TryGetProperty("tag_name", out var tagProperty))
            {
                return null;
            }

            var tag = tagProperty.GetString();
            if (!TryParseVersion(tag, out var latestVersion))
            {
                return null;
            }

            var currentVersion = GetCurrentVersion();
            return new GitHubUpdateResult(
                currentVersion,
                latestVersion,
                tag!,
                latestVersion > currentVersion);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        if (!Version.TryParse(value, out var parsedVersion) || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    private static Version GetCurrentVersion()
    {
        var assembly = typeof(GitHubUpdateService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0];

        return Version.TryParse(informationalVersion, out var version)
            ? version
            : assembly.GetName().Version ?? new Version();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HISA-UpdateChecker/1.0");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
