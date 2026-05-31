using Hisa.Data.Database;
using Hisa.Core.Abstractions;
using Hisa.Esi;
using Hisa.Services.Incursions;
using Hisa.Services.Routing;
using Hisa.Services.Storm;
using Hisa.Services.SystemActivity;
using Hisa.Services.Wormholes;
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
        services.AddHisaEsi();
        services.Configure<StormRefreshOptions>(configuration.GetSection("Hisa:Storms"));
        services.Configure<IncursionRefreshOptions>(options =>
        {
            options.RefreshIntervalMinutes = 5;
        });
        services.Configure<SystemActivityRefreshOptions>(options =>
        {
            options.RefreshIntervalMinutes = 60;
        });
        services.AddHttpClient<IStormCenterSource, EveScoutStormCenterSource>(client =>
        {
            client.BaseAddress = new Uri("https://api.eve-scout.com/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddSingleton<IStormStateService, StormStateService>();
        services.AddHostedService<StormRefreshHostedService>();
        services.AddHttpClient<IHubWormholeSource, EveScoutHubWormholeSource>(client =>
        {
            client.BaseAddress = new Uri("https://api.eve-scout.com/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddSingleton<IHubWormholeStateService, HubWormholeStateService>();
        services.AddHostedService<HubWormholeRefreshHostedService>();
        services.AddSingleton<IIncursionStateService, IncursionStateService>();
        services.AddHostedService<IncursionRefreshHostedService>();
        services.AddSingleton<ISystemActivityStateService, SystemActivityStateService>();
        services.AddHostedService<SystemActivityRefreshHostedService>();
        services.AddSingleton<LocalCharacterLocationLogFeedHostedService>();
        services.AddSingleton<ILocalCharacterLocationFeed>(sp => sp.GetRequiredService<LocalCharacterLocationLogFeedHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<LocalCharacterLocationLogFeedHostedService>());
        services.AddSingleton<IntelChatLogFeedHostedService>();
        services.AddSingleton<IIntelFeed>(sp => sp.GetRequiredService<IntelChatLogFeedHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<IntelChatLogFeedHostedService>());
        services.AddSingleton<ISovUpgradeStateService, SovUpgradeStateService>();
        services.AddSingleton<IAnsiblexNetworkStateService, AnsiblexNetworkStateService>();
        services.AddSingleton<IRouteDistanceService, DijkstraRouteDistanceService>();
        return services;
    }
}
