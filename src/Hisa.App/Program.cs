using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using Avalonia;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hisa.App;

internal static class Program
{
    private static readonly ConcurrentDictionary<string, int> FirstChanceSignatureCounts = new(StringComparer.Ordinal);
    private static readonly object FirstChanceLogFileGate = new();
    private static readonly string FirstChanceLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HISA",
        "logs",
        "first-chance-io.log");
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
        AppDomain.CurrentDomain.FirstChanceException += (_, e) => OnFirstChanceException(e, logger);

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

    private static void OnFirstChanceException(FirstChanceExceptionEventArgs args, ILogger? logger)
    {
        if (args.Exception is not (FileNotFoundException or IOException))
        {
            return;
        }

        var ex = args.Exception;
        var signature = $"{ex.GetType().FullName}|{ex.Message}|{ex.StackTrace?.Split('\n').FirstOrDefault() ?? string.Empty}";
        var count = FirstChanceSignatureCounts.AddOrUpdate(signature, 1, static (_, old) => old + 1);
        if (count > 5)
        {
            return;
        }

        var text = $"[{DateTime.UtcNow:O}] FirstChance {ex.GetType().Name} (count={count}){Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";
        try
        {
            var directory = Path.GetDirectoryName(FirstChanceLogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (FirstChanceLogFileGate)
            {
                File.AppendAllText(FirstChanceLogPath, text);
            }
        }
        catch
        {
            // Best-effort diagnostics only.
        }

        logger?.LogWarning(ex, "First-chance {ExceptionType} (count={Count}). See {LogPath}", ex.GetType().Name, count, FirstChanceLogPath);
    }
}
