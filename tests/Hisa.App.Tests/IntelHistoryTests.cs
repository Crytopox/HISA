using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hisa.App;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Services.Alerts;
using Hisa.Services.Routing;

namespace Hisa.App.Tests;

public sealed class IntelHistoryTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void HistoryIsBoundedDeduplicatedSortedAndExpiresWithoutResettingRows()
    {
        var history = new IntelHistoryViewModel();
        for (var i = 0; i < 355; i++) history.Add(Row(i.ToString(), Now.AddSeconds(-i)), Now);
        Assert.Equal(350, history.Rows.Count);
        Assert.Equal("0", history.VisibleRows[0].Report.DedupeKey);
        Assert.Equal("349", history.VisibleRows[^1].Report.DedupeKey);
        Assert.False(history.Add(Row("0", Now), Now));
        var first = history.VisibleRows[0];
        var collectionChanges = 0;
        history.VisibleRows.CollectionChanged += (_, _) => collectionChanges++;
        history.Tick(Now.AddSeconds(1));
        Assert.Same(first, history.VisibleRows[0]);
        Assert.Equal("00:01", first.Age);
        Assert.Equal(0, collectionChanges);
        history.Configure([], true, 1, Now);
        Assert.Equal(61, history.Rows.Count);
        history.Tick(Now.AddMinutes(2));
        Assert.Empty(history.VisibleRows);
        Assert.Empty(history.Rows);
    }

    [Fact]
    public void ChannelSelectionsAreIndependentAndPersistAnExplicitEmptySubset()
    {
        var history = new IntelHistoryViewModel();
        history.Configure(["Intel", "Other"], true, 30, Now);
        history.Add(Row("1", Now, "Intel"), Now);
        history.Add(Row("2", Now, "Other"), Now);
        Assert.Equal(2, history.VisibleRows.Count);
        history.Channels.Single(c => c.Name == "Other").Selected = false;
        Assert.Equal("Intel", Assert.Single(history.VisibleRows).Report.ChannelName);
        Assert.Equal(2, history.Rows.Count);
        history.Channels.Single(c => c.Name == "Intel").Selected = false;
        Assert.Empty(history.VisibleRows);
        var preferences = JsonSerializer.Deserialize<IntelHistoryPreferences>(JsonSerializer.Serialize(history.GetPreferences()))!;
        Assert.False(preferences.FollowAllChannels);
        Assert.Empty(preferences.SelectedChannels);
        var restored = new IntelHistoryViewModel();
        restored.LoadPreferences(preferences);
        restored.Configure(["Intel", "Other"], true, 30, Now);
        restored.Add(Row("1", Now), Now);
        Assert.Empty(restored.VisibleRows);
        restored.FollowAllChannels = true;
        Assert.Single(restored.VisibleRows);
        restored.Configure(["Other", "New"], true, 30, Now);
        Assert.Empty(restored.Rows);
        Assert.Equal(new[] { "New", "Other" }, restored.Channels.Select(c => c.Name));
        Assert.All(restored.Channels, c => Assert.True(c.Selected));
    }

    [Fact]
    public void ReadAllDiscoversChannelsAndDisabledStateRetainsHistory()
    {
        var history = new IntelHistoryViewModel();
        history.Add(Row("1", Now, "One"), Now);
        history.Add(Row("2", Now, "Two"), Now);
        Assert.Equal(2, history.Channels.Count);
        history.Configure([], false, 30, Now);
        Assert.True(history.IsEmpty);
        Assert.Contains("disabled", history.EmptyMessage);
        Assert.Equal(2, history.Rows.Count);
        history.Configure([], true, 30, Now);
        Assert.Equal(2, history.VisibleRows.Count);
        history.Clear();
        Assert.Empty(history.Rows);
        Assert.Empty(history.VisibleRows);
    }

    [AvaloniaTheory]
    [InlineData(380, 520)]
    [InlineData(280, 240)]
    [InlineData(280, 160)]
    public void CompactWindowRendersOriginalMessagesAndOptionalMetadata(int width, int height)
    {
        var history = new IntelHistoryViewModel();
        history.Add(Row("plain", DateTime.UtcNow, text: "Status? Watching the gate. A long original message should wrap neatly inside the compact overlay without losing its text."), DateTime.UtcNow);
        var report = Report("parsed", DateTime.UtcNow.AddSeconds(-35), text: "E-JCUS 3 Sabres, bubble on the gate", alerts: [IntelAlertType.Bubble]);
        history.Add(new IntelHistoryRow
        {
            Report = report, Systems = [new("E-JCUS", 1)],
            Details = Details(report)
        }, DateTime.UtcNow);
        var window = new IntelHistoryWindow { DataContext = history, Width = width, Height = height };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var list = window.FindControl<ListBox>("HistoryList")!;
            Assert.Equal(2, list.ItemCount);
            Assert.Contains(list.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text!.StartsWith("Status?"));
            Assert.Equal(280, window.MinWidth); Assert.Equal(160, window.MinHeight);
            Assert.True(window.Topmost); Assert.False(window.ShowActivated);
            if (width == 380)
            {
                var parsedRow = list.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.DataContext is IntelHistoryRow row && row.Report.DedupeKey == "parsed");
                Assert.InRange(parsedRow.Bounds.Height, 30, 78);
            }
            using var image = new RenderTargetBitmap(new(width, height)); image.Render(window);
            image.Save(Path.Combine(AppContext.BaseDirectory, $"intel-history-{width}-{height}.png"));
            window.Close(); Assert.False(window.IsVisible);
            window.Show(); Assert.True(window.IsVisible);
        }
        finally { window.Shutdown(); }
    }

    [AvaloniaFact]
    public async Task PointerResizePersistsAndReopensAtResizedDimensions()
    {
        using var fixture = new MainFixture();
        var window = new IntelHistoryWindow(fixture.Vm);
        try
        {
            await window.ShowHistoryAsync(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var width = window.Bounds.Width; var height = window.Bounds.Height;
            var grip = window.FindControl<Border>("ResizeSouthEast")!;
            var start = grip.TranslatePoint(new Point(8, 8), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(40, 25));
            window.MouseUp(start + new Vector(40, 25), MouseButton.Left);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(width + 40, window.Width); Assert.Equal(height + 25, window.Height);
            window.Close();
            var saved = await fixture.Vm.GetIntelHistoryPlacementAsync();
            Assert.NotNull(saved); Assert.Equal(width + 40, saved.Width); Assert.Equal(height + 25, saved.Height);
            await window.ShowHistoryAsync();
            Assert.Equal(saved.Width, window.Width); Assert.Equal(saved.Height, window.Height);
        }
        finally { window.Shutdown(); }
    }

    [AvaloniaTheory]
    [InlineData("East", 35, 0, 415, 520, 100, 100)]
    [InlineData("West", -35, 0, 415, 520, 65, 100)]
    [InlineData("North", 0, -25, 380, 545, 100, 75)]
    [InlineData("South", 0, 25, 380, 545, 100, 100)]
    [InlineData("NorthWest", -35, -25, 415, 545, 65, 75)]
    [InlineData("NorthEast", 35, -25, 415, 545, 100, 75)]
    [InlineData("SouthWest", -35, 25, 415, 545, 65, 100)]
    [InlineData("SouthEast", 35, 25, 415, 545, 100, 100)]
    [InlineData("SouthEast", -500, -700, 280, 160, 100, 100)]
    public void EveryEdgeAndCornerResizesThroughActualPointerGestures(string edge, int dx, int dy, int width, int height, int x, int y)
    {
        var window = new IntelHistoryWindow { DataContext = new IntelHistoryViewModel(), Position = new PixelPoint(100, 100) };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var handle = window.FindControl<Border>($"Resize{edge}")!;
            var start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(dx, dy));
            window.MouseUp(start + new Vector(dx, dy), MouseButton.Left);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(width, window.Width); Assert.Equal(height, window.Height);
            Assert.Equal(new PixelPoint(x, y), window.Position);
        }
        finally { window.Shutdown(); }
    }

    [AvaloniaFact]
    public void HeaderTitleAreaHandlesMoveGesture()
    {
        var window = new IntelHistoryWindow { DataContext = new IntelHistoryViewModel() };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var handled = false;
            window.AddHandler(InputElement.PointerPressedEvent, (_, args) => handled = args.Handled, RoutingStrategies.Bubble, handledEventsToo: true);
            window.MouseDown(new Point(75, 15), MouseButton.Left);
            window.MouseUp(new Point(75, 15), MouseButton.Left);
            Assert.True(handled);
        }
        finally { window.Shutdown(); }
    }

    [AvaloniaFact]
    public async Task IntelSettingsPersistExclusionsAndExplicitChannelSubset()
    {
        using var fixture = new MainFixture();
        var vm = fixture.Vm;
        vm.IntelIncludeChannelsText = "Intel\nOther";
        vm.IntelNotificationExclusionsText = " clear \nCLR\nclear\n\n status ";
        vm.IntelHistory.Configure(["Intel", "Other"], true, 30, DateTime.UtcNow);
        vm.IntelHistory.Channels.Single(c => c.Name == "Other").Selected = false;
        await vm.SaveIntelSettingsAsync();
        Assert.Equal("Intel settings saved and applied.", vm.StatusText);
        Assert.Equal(new[] { "clear", "CLR", "status" }, await fixture.Settings.GetAsync<List<string>>("Intel.NotificationExclusions"));
        var saved = await fixture.Settings.GetAsync<IntelHistoryPreferences>("Intel.History.Channels");
        Assert.NotNull(saved); Assert.False(saved.FollowAllChannels); Assert.Equal(new[] { "Intel" }, saved.SelectedChannels);
        vm.IntelNotificationExclusionsText = "";
        await (Task)typeof(MainWindowViewModel).GetMethod("InitializeIntelHistoryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [])!;
        Assert.Equal(string.Join(Environment.NewLine, "clear", "CLR", "status"), vm.IntelNotificationExclusionsText);
        Assert.False(vm.IntelHistory.FollowAllChannels);
        // Empty include lists retain the feed's supported read-all mode.
        vm.IntelIncludeChannelsText = "";
        await vm.SaveIntelSettingsAsync();
        Assert.Equal("Intel settings saved and applied.", vm.StatusText);
        var settingsWindow = new IntelSettingsWindow(vm);
        settingsWindow.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); settingsWindow.UpdateLayout();
            Assert.DoesNotContain(settingsWindow.GetVisualDescendants().OfType<TextBlock>(), text => text.Text is "Width" or "Height" or "Intel History overlay");
            using var image = new RenderTargetBitmap(new((int)settingsWindow.Bounds.Width, (int)settingsWindow.Bounds.Height));
            image.Render(settingsWindow); image.Save(Path.Combine(AppContext.BaseDirectory, "intel-settings.png"));
        }
        finally { settingsWindow.Close(); }
    }

    [AvaloniaFact]
    public void NewArrivalsPreserveReadingPositionAndOfferReturnToNewest()
    {
        var history = new IntelHistoryViewModel();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 100; i++) history.Add(Row(i.ToString(), now.AddSeconds(-i), text: "A report with enough content to wrap across several lines in the history overlay."), now);
        var window = new IntelHistoryWindow { DataContext = history };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var list = window.FindControl<ListBox>("HistoryList")!;
            list.ScrollIntoView(history.VisibleRows[40]);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Offset.Y > 0);
            var before = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Where(i => i.TranslatePoint(default, list) is { } p && p.Y + i.Bounds.Height > 0)
                .OrderBy(i => i.TranslatePoint(default, list)!.Value.Y).First();
            var anchor = before.DataContext;
            var y = before.TranslatePoint(default, list)!.Value.Y;
            history.Add(Row("new", now.AddSeconds(1)), now);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var button = window.FindControl<Button>("NewReportsButton")!;
            Assert.True(button.IsVisible);
            var after = list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => ReferenceEquals(i.DataContext, anchor));
            Assert.InRange(Math.Abs(after.TranslatePoint(default, list)!.Value.Y - y), 0, 2);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(button.IsVisible); Assert.Equal(0, scroll.Offset.Y);
        }
        finally { window.Shutdown(); }
    }

    [AvaloniaFact]
    public async Task ExclusionsPreventMapPopupAndRuleActionsWithoutRemovingReportsOrConsumingAlertDedupe()
    {
        using var fixture = new MainFixture();
        var vm = fixture.Vm;
        var triggered = new List<AlertTriggered>();
        vm.AlertTriggered += (_, alert) => triggered.Add(alert);
        vm.IntelNotificationExclusionsText = "clear\nstatus";
        var now = DateTime.UtcNow;
        var suppressed = Report("suppressed", now, text: "E-JCUS CLEAR!", alerts: [IntelAlertType.Clear], clear: true);
        fixture.Feed.Publish(suppressed);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(triggered); Assert.Empty(vm.MapReportPopupRequests);
        Assert.Single(vm.IntelHistory.VisibleRows); Assert.Single(vm.IntelCardsForView);
        // The same report can still trigger the engine after removing exclusions:
        // the suppression path has not marked it emitted or started its cooldown.
        vm.IntelNotificationExclusionsText = "";
        typeof(MainWindowViewModel).GetMethod("EvaluateAlertRules", BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(IntelChatReport), typeof(bool)])!.Invoke(vm, [suppressed, false]);
        Dispatcher.UIThread.RunJobs();
        var alert = Assert.Single(triggered);
        Assert.Contains(AlertActionType.PlaySound, alert.Actions);
        Assert.Contains(AlertActionType.ShowPopup, alert.Actions);
        fixture.Feed.Publish(Report("normal", now.AddSeconds(1), text: "E-JCUS hostiles"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, triggered.Count); Assert.Single(vm.MapReportPopupRequests);
        Assert.Equal(2, vm.IntelHistory.Rows.Count);
        await vm.ClearIntelAndKillmailHistoryAsync();
        Assert.Empty(vm.IntelHistory.Rows); Assert.Empty(vm.MapReportPopupRequests);
    }

    [AvaloniaFact]
    public async Task OverlayPlacementAndSizeRoundTripAndHiddenWindowCanReopen()
    {
        using var fixture = new MainFixture();
        var vm = fixture.Vm;
        await vm.SaveIntelHistoryPlacementAsync(new Hisa.App.WindowPlacementState
        {
            Width = 410, Height = 390, PositionX = -99999, PositionY = -99999, WindowState = "Normal"
        });
        var window = new IntelHistoryWindow(vm);
        try
        {
            await window.ShowHistoryAsync(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(410, window.Width); Assert.Equal(390, window.Height);
            Assert.True(window.Position.X > -99999); Assert.True(window.Position.Y > -99999);
            Assert.True(await fixture.Settings.GetAsync<bool>("Intel.History.Visible"));
            window.Width = 450;
            window.Height = 220;
            window.Position = new PixelPoint(55, 66);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(450, window.Width);
            window.Close(); Assert.False(window.IsVisible);
            Assert.False(await fixture.Settings.GetAsync<bool>("Intel.History.Visible"));
            await window.ShowHistoryAsync(); Assert.True(window.IsVisible);
            var placement = await vm.GetIntelHistoryPlacementAsync();
            Assert.NotNull(placement); Assert.Equal(450, placement.Width);
            Assert.Equal(220, placement.Height);
            Assert.Equal(window.Position.X, placement.PositionX);
            Assert.NotNull(placement.ScreenWorkingAreaX);
            Assert.NotNull(placement.ScreenScaling);
        }
        finally { window.Shutdown(); }
        Assert.True(await fixture.Settings.GetAsync<bool>("Intel.History.Visible"));
        await (Task)typeof(MainWindowViewModel).GetMethod("InitializeIntelHistoryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [])!;
        Assert.True(vm.ShouldRestoreIntelHistoryVisible);
    }

    [Fact]
    public void PlacementTracksMonitorAcrossOriginAndDpiChangesAndRecoversMissingMonitor()
    {
        var saved = new Hisa.App.WindowPlacementState
        {
            Width = 380, Height = 300, PositionX = -2260, PositionY = 150, WindowState = "Normal",
            ScreenDisplayName = "Left", ScreenScaling = 1.5, ScreenOffsetX = 300, ScreenOffsetY = 150,
            ScreenWorkingAreaX = -2560, ScreenWorkingAreaY = 0, ScreenWorkingAreaWidth = 2560, ScreenWorkingAreaHeight = 1400
        };
        var primary = new IntelHistoryScreen("Primary", new(0, 0, 1920, 1040), 1, true);
        var moved = IntelHistoryPlacement.Restore(saved, [primary, new("Left", new(1920, -200, 3840, 2040), 2, false)]);
        Assert.Equal("Left", moved.ScreenDisplayName);
        Assert.Equal(2320, moved.PositionX); Assert.Equal(0, moved.PositionY);
        Assert.Equal(380, moved.Width); Assert.Equal(300, moved.Height);
        var missing = IntelHistoryPlacement.Restore(saved, [primary]);
        Assert.Equal("Primary", missing.ScreenDisplayName);
        Assert.InRange(missing.PositionX, 0, 1540); Assert.InRange(missing.PositionY, 0, 740);
        var negative = IntelHistoryPlacement.Restore(saved, [primary, new("Left", new(-2560, -100, 2560, 1400), 1.5, false)]);
        Assert.Equal(-2260, negative.PositionX); Assert.Equal(50, negative.PositionY);
        var identicalNames = IntelHistoryPlacement.Restore(saved,
            [new("Left", new(0, 0, 1920, 1040), 1, true), new("Left", new(-2560, 0, 2560, 1400), 1.5, false)]);
        Assert.Equal(-2260, identicalNames.PositionX);
    }

    private static IntelHistoryRow Row(string key, DateTime time, string channel = "Intel", string text = "status?") => new()
    {
        Report = Report(key, time, channel, text), Systems = []
    };

    private static IntelChatReport Report(string key, DateTime time, string channel = "Intel", string text = "status?", IReadOnlyList<IntelAlertType>? alerts = null, bool clear = false) => new()
    {
        DedupeKey = key, TimestampUtc = time, ChannelName = channel, ReporterName = "Scout",
        MessageText = text, SourceFilePath = "Intel.txt", Systems = ["E-JCUS"], ShipClasses = [],
        ReportedShipNames = [], ReportedShipTypeIds = [], Alerts = alerts ?? [], ReportedHostileNames = [], IsClear = clear
    };

    private static IntelOverlayCard Details(IntelChatReport report) => new()
    {
        SortTimestampUtc = report.TimestampUtc, LastUpdatedUtc = report.TimestampUtc, SolarSystemId = 1,
        SystemName = "E-JCUS", ConstellationName = "", RegionName = "", ChannelName = report.ChannelName,
        ReporterName = report.ReporterName, AgeSummary = "", MessageText = report.MessageText,
        Hostiles = [new() { Name = "A pilot" }], ShipsSummary = [new() { ShipName = "Sabre", Count = 3 }],
        ShipClassSummary = "Destroyer", HostileCount = 3, ShipBadgeBackgroundHex = "#000000",
        ShipBadgeBorderHex = "#000000", HostileBadgeBackgroundHex = "#000000", HostileBadgeBorderHex = "#000000", AccentHex = "#FFB347"
    };

    private sealed class MainFixture : IDisposable
    {
        public Feed Feed { get; } = new();
        public MemorySettings Settings { get; } = new();
        public MainWindowViewModel Vm { get; }
        public MainFixture()
        {
            Vm = new(Stub<IMapDataService>(), Settings, Stub<IStormStateService>(), Stub<IHubWormholeStateService>(),
                Stub<ISovUpgradeStateService>(), Stub<IMiningSiteTrackerService>(), Stub<IAnsiblexNetworkStateService>(),
                Stub<IIncursionStateService>(), Stub<ISystemActivityStateService>(), Stub<ILocalCharacterLocationFeed>(),
                Feed, Stub<IMiningSessionFeed>(), new AlertRuleEngine(new DijkstraRouteDistanceService()));
            typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.CurrentGraph))!.SetValue(Vm,
                new MapGraph { Nodes = [new() { Id = 1, Name = "E-JCUS", X = 0, Y = 0 }], Links = [] });
            typeof(MainWindowViewModel).GetField("_alertRules", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Vm,
                new List<AlertRule> { new() { Id = "all", Name = "All intel", ShowClearIntelReports = true, Actions = [AlertActionType.ShowPopup, AlertActionType.PlaySound] } });
        }
        public void Dispose()
        {
            Vm.BeginApplicationShutdown();
            foreach (var field in typeof(MainWindowViewModel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                if (field.GetValue(Vm) is DispatcherTimer timer) timer.Stop();
        }
    }

    private sealed class Feed : IIntelFeed
    {
        public event EventHandler<IntelChatReport>? ChannelMessageReceived;
        public event EventHandler<IntelChatReport>? ReportReceived;
        public event EventHandler<IReadOnlyDictionary<long, IntelSystemSnapshot>>? SnapshotUpdated { add { } remove { } }
        public IReadOnlyDictionary<long, IntelSystemSnapshot> Snapshot => new Dictionary<long, IntelSystemSnapshot>();
        public string? GetShipTypeName(int id) => null;
        public Task ApplySettingsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Publish(IntelChatReport report) { ChannelMessageReceived?.Invoke(this, report); ReportReceived?.Invoke(this, report); }
    }
    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(_values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) { _values[key] = JsonSerializer.Serialize(value); return Task.CompletedTask; }
    }
    private static T Stub<T>() where T : class => DispatchProxy.Create<T, StubProxy>();
    public class StubProxy : DispatchProxy
    {
        // Hold initial map loading so these focused tests control all state and perform no I/O.
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "GetRegionsAsync" => new TaskCompletionSource<IReadOnlyList<RegionOption>>().Task,
            "get_CurrentLinks" => Array.Empty<AnsiblexLinkEntry>(),
            _ when method.ReturnType == typeof(Task) => Task.CompletedTask,
            _ => null
        };
    }
}
