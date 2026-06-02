using System.Diagnostics;

namespace Hisa.App.Services;

public static class ExternalUrlLauncher
{
    public static void Open(string url)
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
