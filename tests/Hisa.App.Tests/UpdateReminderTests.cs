using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Hisa.App;
using Hisa.App.Services;
using Hisa.Core.Abstractions;

namespace Hisa.App.Tests;

public sealed class UpdateReminderTests
{
    private static readonly GitHubUpdateResult Available = new(new Version(1, 11, 0), new Version(1, 12, 0), "v1.12.0", true);

    [AvaloniaFact]
    public void ReminderUsesDebugPreviewOrRequiresANewerVersion()
    {
        var window = new MainWindow();
        try
        {
            var button = window.FindControl<Button>("UpdateReminderButton")!;
            AssertFallback(button);
            Apply(window, Available);
            Assert.True(button.IsVisible);
            Assert.Equal("Update available · v1.12.0", button.Content);
            Assert.Equal("Installed: v1.11.0    Latest: v1.12.0\nOpen GitHub Releases", ToolTip.GetTip(button));
            Apply(window, Available with { IsUpdateAvailable = false });
            AssertFallback(button);
            Apply(window, null);
            AssertFallback(button);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartupReminderPersistsIndependentlyOfPopupAcknowledgement(bool alreadyAcknowledged)
    {
        var settings = new ReminderSettings(alreadyAcknowledged ? Available.LatestTag : null);
        var service = new GitHubUpdateService(settings);
        typeof(GitHubUpdateService).GetField("_cachedCheck", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, Task.FromResult<GitHubUpdateResult?>(Available));
        var window = new MainWindow();
        typeof(MainWindow).GetField("_updateService", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, service);
        KeepNavbarOnly(window);
        window.Show();
        try
        {
            typeof(MainWindow).GetMethod("OnCheckForUpdatesOnStartup", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [window, EventArgs.Empty]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Button>("UpdateReminderButton")!.IsVisible);
            var popups = window.OwnedWindows.OfType<UpdateAvailableWindow>().ToArray();
            Assert.Equal(alreadyAcknowledged ? 0 : 1, popups.Length);
            foreach (var popup in popups) popup.Close();
            Assert.True(window.FindControl<Button>("UpdateReminderButton")!.IsVisible);
            Assert.Equal(Available.LatestTag, settings.Tag);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(800)]
    [InlineData(1200)]
    public void LongStatusDoesNotOverlapReminderOrNavigation(double width)
    {
        var window = new MainWindow();
        KeepNavbarOnly(window);
        window.Show();
        try
        {
            Apply(window, Available);
            var button = window.FindControl<Button>("UpdateReminderButton")!;
            var grid = (Grid)button.Parent!;
            var status = grid.Children.OfType<TextBlock>().Single();
            status.IsVisible = true;
            status.Text = new string('W', 1000);
            grid.Measure(new Size(width, 30));
            grid.Arrange(new Rect(0, 0, width, 30));
            var navigation = grid.Children.OfType<StackPanel>().Single();
            Assert.True(button.Bounds.Width > 0);
            Assert.True(status.Bounds.Left >= button.Bounds.Right);
            Assert.True(status.Bounds.Right <= navigation.Bounds.Left);
            Assert.True(navigation.Bounds.Right <= width);
            grid.IsVisible = false;
            Assert.False(button.IsEffectivelyVisible);
            grid.IsVisible = true;
            Assert.True(button.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    private static void Apply(MainWindow window, GitHubUpdateResult? update)
        => typeof(MainWindow).GetMethod("UpdatePersistentReminder", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [update]);

    private static void AssertFallback(Button button)
    {
#if DEBUG
        Assert.True(button.IsVisible);
        Assert.Equal("Update available · preview", button.Content);
        Assert.Contains("Debug only", Assert.IsType<string>(ToolTip.GetTip(button)));
#else
        Assert.False(button.IsVisible);
#endif
    }

    private static void KeepNavbarOnly(MainWindow window)
    {
        // Exercise the real navbar without rendering the unrelated map/font pipeline.
        var navbar = (Grid)window.FindControl<Button>("UpdateReminderButton")!.Parent!;
        ((Border)navbar.Parent!).Child = null;
        window.Content = navbar;
    }

    private sealed class ReminderSettings(string? tag) : ISettingsService
    {
        public string? Tag { get; private set; } = tag;
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(Tag is T value ? value : default);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            Tag = value as string;
            return Task.CompletedTask;
        }
    }
}
