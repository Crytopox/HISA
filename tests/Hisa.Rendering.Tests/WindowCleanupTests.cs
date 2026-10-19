using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hisa.App.Services;

namespace Hisa.Rendering.Tests;

public sealed class WindowCleanupTests
{
    [AvaloniaFact]
    public async Task ClosingKeepsWindowAndDispatcherAlive_UntilCleanupFinishes_AndRunsOnlyOnce()
    {
        var window = new Window(); var calls = 0; var closed = false;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = new WindowCleanup(window, async () => { calls++; closing.TrySetResult(); await release.Task; }, () => { }, TimeSpan.FromSeconds(2));
        window.Closed += (_, _) => { closed = true; finished.TrySetResult(); };
        window.Show(); window.Close();
        await closing.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(closed); Assert.True(window.IsVisible);
        window.Close(); Assert.Equal(1, calls); Assert.False(closed);
        release.SetResult(); await finished.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(closed); Assert.Equal(1, calls);
    }
    [AvaloniaFact]
    public async Task ClosingCannotHangForeverOnUnresponsiveCleanup()
    {
        var window = new Window();
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = new WindowCleanup(window, () => never.Task, () => { }, TimeSpan.FromMilliseconds(20));
        window.Closed += (_, _) => finished.TrySetResult();
        window.Show(); window.Close(); await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        never.SetResult(); Assert.False(window.IsVisible);
    }
}
