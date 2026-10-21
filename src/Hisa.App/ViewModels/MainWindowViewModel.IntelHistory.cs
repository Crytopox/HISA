using System.ComponentModel;
using Avalonia.Threading;
using Hisa.Core;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed partial class MainWindowViewModel
{
    private const string IntelNotificationExclusionsKey = "Intel.NotificationExclusions";
    private const string IntelHistoryPreferencesKey = "Intel.History.Channels";
    private const string IntelHistoryPlacementKey = "Window.IntelHistory.Placement";
    private const string IntelHistoryWidthKey = "Intel.History.Width";
    private const string IntelHistoryHeightKey = "Intel.History.Height";
    private const string IntelHistoryVisibleKey = "Intel.History.Visible";
    private IntelNotificationFilter _intelNotificationFilter = new(null);
    private string _intelNotificationExclusionsText = string.Empty;
    internal (int Width, int Height) IntelHistoryDefaultSize { get; private set; } = (380, 520);
    public bool ShouldRestoreIntelHistoryVisible { get; private set; }
    private readonly SemaphoreSlim _intelHistorySaveGate = new(1, 1);
    private readonly SemaphoreSlim _intelHistoryVisibilitySaveGate = new(1, 1);
    public IntelHistoryViewModel IntelHistory { get; } = new();

    public string IntelNotificationExclusionsText
    {
        get => _intelNotificationExclusionsText;
        set
        {
            if (SetProperty(ref _intelNotificationExclusionsText, value))
                _intelNotificationFilter = new IntelNotificationFilter(value);
        }
    }
    private async Task InitializeIntelHistoryAsync()
    {
        IntelNotificationExclusionsText = string.Join(Environment.NewLine,
            await _settingsService.GetAsync<List<string>>(IntelNotificationExclusionsKey) ?? []);
        // Legacy numeric settings are only a migration fallback; manual placement owns size now.
        IntelHistoryDefaultSize = (Math.Clamp(await _settingsService.GetAsync<int?>(IntelHistoryWidthKey) ?? 380, 280, 2400),
            Math.Clamp(await _settingsService.GetAsync<int?>(IntelHistoryHeightKey) ?? 520, 160, 2400));
        ShouldRestoreIntelHistoryVisible = await _settingsService.GetAsync<bool?>(IntelHistoryVisibleKey) ?? false;
        IntelHistory.LoadPreferences(await _settingsService.GetAsync<IntelHistoryPreferences>(IntelHistoryPreferencesKey) ?? new());
        ConfigureIntelHistory();
    }

    internal void ConfigureIntelHistory() => IntelHistory.Configure(GetConfiguredIntelIncludeChannels(), IntelEnabled, IntelListExpiryMinutes, DateTime.UtcNow);

    private async Task SaveIntelHistorySettingsAsync()
    {
        IntelNotificationExclusionsText = string.Join(Environment.NewLine, _intelNotificationFilter.Entries);
        await _settingsService.SetAsync(IntelNotificationExclusionsKey, _intelNotificationFilter.Entries.ToList());
        ConfigureIntelHistory();
        await PersistIntelHistoryPreferencesAsync();
    }

    public async Task SetIntelHistoryVisibleAsync(bool visible)
    {
        ShouldRestoreIntelHistoryVisible = visible;
        await _intelHistoryVisibilitySaveGate.WaitAsync();
        try { await _settingsService.SetAsync(IntelHistoryVisibleKey, visible); }
        finally { _intelHistoryVisibilitySaveGate.Release(); }
    }

    private async void OnIntelHistoryPreferencesChanged(object? sender, EventArgs e) => await PersistIntelHistoryPreferencesAsync();

    private async Task PersistIntelHistoryPreferencesAsync()
    {
        await _intelHistorySaveGate.WaitAsync();
        try { await _settingsService.SetAsync(IntelHistoryPreferencesKey, IntelHistory.GetPreferences()); }
        finally { _intelHistorySaveGate.Release(); }
    }

    public Task<WindowPlacementState?> GetIntelHistoryPlacementAsync() => _settingsService.GetAsync<WindowPlacementState>(IntelHistoryPlacementKey);
    public Task SaveIntelHistoryPlacementAsync(WindowPlacementState placement) => _settingsService.SetAsync(IntelHistoryPlacementKey, placement);

    private void OnIntelChannelMessageReceived(object? sender, IntelChatReport report)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsApplicationShuttingDown) return;
            // UI row enrichment is optional. Always retain the original message.
            IntelOverlayCard? details = null;
            try
            {
                details = BuildIntelOverlayCard(report, BuildGraphNodeByNameLookup(CurrentGraph), new HashSet<long>(),
                    new Dictionary<string, IntelSystemSnapshot>(), limitToVisibleRegion: false, applyCachedIdentityData: true);
            }
            catch { /* Original text remains available if a display asset cannot be built. */ }
            var row = new IntelHistoryRow
            {
                Report = report, Details = details,
                Systems = report.Systems.Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => new IntelHistorySystem(name, ResolveSystemIdByName(name))).ToList()
            };
            if (IntelHistory.Add(row, DateTime.UtcNow))
            {
                _ = ResolveIntelCharacterIdsAsync();
                _ = EnsureShipImagesForIntelCardsAsync();
                _ = EnsureIntelIdentityAssetsAsync();
            }
        });
    }

    private IEnumerable<IntelOverlayCard> IntelAssetCards => _intelCardsForView.Concat(
        IntelHistory.Rows.Where(row => row.Details is not null).Select(row => row.Details!));
}
