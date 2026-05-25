using System;
using Avalonia;
using Microsoft.Extensions.Hosting;

namespace Hisa.App;

internal static class Program
{
    public static IHost? Host { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        Host = AppHostBuilder.Build(args);
        Host.Start();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        Host.StopAsync().GetAwaiter().GetResult();
        Host.Dispose();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
