using Hisa.Data.Database;
using Hisa.Core.Abstractions;
using Hisa.Services.Storm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Background;

public sealed class DatabaseInitializationHostedService : IHostedService
{
    private readonly IDatabaseInitializer _databaseInitializer;
    private readonly ILogger<DatabaseInitializationHostedService> _logger;

    public DatabaseInitializationHostedService(
        IDatabaseInitializer databaseInitializer,
        ILogger<DatabaseInitializationHostedService> logger)
    {
        _databaseInitializer = databaseInitializer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing SQLite database...");
        await _databaseInitializer.InitializeAsync(cancellationToken);
        _logger.LogInformation("SQLite database initialized.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHisaServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<DatabaseInitializationHostedService>();
        services.Configure<StormRefreshOptions>(configuration.GetSection("Hisa:Storms"));
        services.AddHttpClient<IStormCenterSource, EveScoutStormCenterSource>(client =>
        {
            client.BaseAddress = new Uri("https://api.eve-scout.com/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddSingleton<IStormStateService, StormStateService>();
        services.AddHostedService<StormRefreshHostedService>();
        return services;
    }
}
