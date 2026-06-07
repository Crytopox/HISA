using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
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
    private static readonly bool FirstChanceIoLoggingEnabled =
        #if DEBUG
        true;
        #else
        Debugger.IsAttached || IsEnabledByEnvironment("HISA_ENABLE_FIRST_CHANCE_IO_LOGGING");
        #endif
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
        if (FirstChanceIoLoggingEnabled)
        {
            AppDomain.CurrentDomain.FirstChanceException += (_, e) => OnFirstChanceException(e, logger);
        }

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
        if (IsExpectedTransportShutdownException(ex))
        {
            return;
        }

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

    private static bool IsExpectedTransportShutdownException(Exception ex)
    {
        if (ex is not IOException io)
        {
            return false;
        }

        if (io.InnerException is SocketException socketEx && socketEx.ErrorCode == 995)
        {
            return true;
        }

        if (io.InnerException is ObjectDisposedException od &&
            (string.Equals(od.ObjectName, "System.Net.Sockets.NetworkStream", StringComparison.Ordinal) ||
             string.Equals(od.ObjectName, "System.Net.Security.SslStream", StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    private static bool IsEnabledByEnvironment(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
