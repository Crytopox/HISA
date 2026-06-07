using System.IO;
using Hisa.App.Diagnostics;
using Hisa.App.Services;
using Hisa.Data.Database;
using Hisa.Services;
using System.Diagnostics;
using Hisa.Services.Background;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hisa.App;

internal static class AppHostBuilder
{
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "HISA_");

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(ResolveMinimumLogLevel(builder.Configuration));
        builder.Logging.AddConsole();
        var appLogStore = new AppLogStore();
        builder.Logging.AddProvider(new InMemoryLoggerProvider(appLogStore));

        builder.Services.AddSingleton(appLogStore);
        builder.Services.AddSingleton<IAppLogFileService, AppLogFileService>();
        builder.Services.AddSingleton<GitHubUpdateService>();
        builder.Services.AddSingleton<KillmailAppService>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<MapEditorViewModel>();
        builder.Services.AddSingleton<MapEditorWindow>();
        builder.Services.AddSingleton<DebugWindowViewModel>();
        builder.Services.AddSingleton<DebugWindow>();

        builder.Services.AddHisaData(builder.Configuration);
        builder.Services.AddHisaServices(builder.Configuration);
        builder.Services.AddHisaMapServices();

        return builder.Build();
    }

    private static LogLevel ResolveMinimumLogLevel(IConfiguration configuration)
    {
        var configured = configuration["Hisa:Diagnostics:MinimumLogLevel"];
        if (!string.IsNullOrWhiteSpace(configured) && Enum.TryParse<LogLevel>(configured, true, out var parsed))
        {
            return parsed;
        }

        #if DEBUG
        return LogLevel.Trace;
        #else
        return Debugger.IsAttached ? LogLevel.Trace : LogLevel.Information;
        #endif
    }
}
