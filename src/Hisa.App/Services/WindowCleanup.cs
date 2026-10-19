using Avalonia.Controls;
using Hisa.Core;

namespace Hisa.App.Services;

/// <summary>Keep the desktop lifetime and UI dispatcher alive until bounded async cleanup completes.</summary>
public sealed class WindowCleanup
{
    private bool _started, _complete;
    public WindowCleanup(Window window, Func<Task> cleanup, Action begin, TimeSpan timeout)
    {
        window.Closing += async (_, args) =>
        {
            if (_complete) return;
            args.Cancel = true;
            if (_started) return;
            _started = true;
            begin();
            await Task.Yield(); // Close again only after the original Closing event has unwound.
            await BoundedCleanup.RunAsync(cleanup, timeout);
            _complete = true;
            window.Close();
        };
    }
}
