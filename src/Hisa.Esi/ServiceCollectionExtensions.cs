using Hisa.Core.Abstractions;
using Hisa.Esi.Auth;
using Hisa.Esi.Clients;
using Hisa.Esi.Options;
using Hisa.Esi.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace Hisa.Esi;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHisaEsi(this IServiceCollection services)
    {
        services.Configure<EsiOptions>(options =>
        {
            options.BaseUrl = EsiInternalDefaults.BaseUrl;
            options.UserAgent = EsiInternalDefaults.UserAgent;
            options.CompatibilityDate = EsiInternalDefaults.CompatibilityDate;
            options.OAuth.Enabled = EsiInternalDefaults.OAuthEnabled;
            options.OAuth.AuthorizationEndpoint = EsiInternalDefaults.AuthorizationEndpoint;
            options.OAuth.TokenEndpoint = EsiInternalDefaults.TokenEndpoint;
            options.OAuth.ClientId = EsiInternalDefaults.ClientId;
            options.OAuth.ClientSecret = EsiInternalDefaults.ClientSecret;
            options.OAuth.CallbackUrl = EsiInternalDefaults.CallbackUrl;
            options.Incursions.CacheSeconds = EsiInternalDefaults.IncursionCacheSeconds;
            options.Incursions.TokenLimitPer15Minutes = EsiInternalDefaults.IncursionTokenLimitPer15Minutes;
        });
        services.AddSingleton<IEsiMetricsStore, EsiMetricsStore>();
        services.AddHttpClient<IEsiAuthService, EsiAuthService>((sp, client) =>
        {
            ApplyCommonHeaders(sp, client);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddTransient<IEsiAccessTokenProvider, EsiAccessTokenProvider>();
        services.AddHttpClient<IEsiPublicClient, EsiPublicClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<EsiOptions>>().CurrentValue;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            ApplyCommonHeaders(sp, client);
        });

        return services;
    }

    private static void ApplyCommonHeaders(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<EsiOptions>>().CurrentValue;
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
        if (!string.IsNullOrWhiteSpace(options.CompatibilityDate))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Compatibility-Date", options.CompatibilityDate);
        }
    }
}
