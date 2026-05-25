using System.IO;
using Hisa.Data.Database;
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
        builder.Logging.AddConsole();

        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        builder.Services.AddHisaData(builder.Configuration);
        builder.Services.AddHisaServices();

        return builder.Build();
    }
}
