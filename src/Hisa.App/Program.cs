using System;
using Avalonia;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hisa.App;

internal static class Program
{
    public static IHost? Host { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        Host = AppHostBuilder.Build(args);
        Host.Start();
        var loggerFactory = Host.Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        var logger = loggerFactory?.CreateLogger("Program");
        WireGlobalExceptionLogging(logger);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        Host.StopAsync().GetAwaiter().GetResult();
        Host.Dispose();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static void WireGlobalExceptionLogging(ILogger? logger)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                logger?.LogCritical(ex, "Unhandled app-domain exception.");
            }
            else
            {
                logger?.LogCritical("Unhandled app-domain exception object: {ExceptionObject}", e.ExceptionObject);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger?.LogError(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        };
    }
}
