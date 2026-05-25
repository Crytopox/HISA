using Hisa.Data.Database;
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
    public static IServiceCollection AddHisaServices(this IServiceCollection services)
    {
        services.AddHostedService<DatabaseInitializationHostedService>();
        return services;
    }
}
