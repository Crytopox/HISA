using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hisa.App.Services;

public sealed class KillmailAppService
{
    private static readonly Uri BattleReportsEndpoint = new("https://killmail.app/api/battle-reports");
    private const string LandingPageUrl = "https://killmail.app/";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<string?> CreateBattleReportLaunchTargetAsync(long solarSystemId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var periodStart = nowUtc.AddHours(-1);
        var periodEnd = nowUtc.AddHours(1);
        var request = new BattleReportRequest
        {
            SolarSystemId = solarSystemId,
            PeriodStart = periodStart.UtcDateTime,
            PeriodEnd = periodEnd.UtcDateTime,
            SideOverrides = []
        };

        using var response = await HttpClient.PostAsJsonAsync(
            BattleReportsEndpoint,
            request,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("external_id", out var externalIdElement) ||
            externalIdElement.ValueKind != JsonValueKind.String)
        {
            return LandingPageUrl;
        }

        if (!document.RootElement.TryGetProperty("location", out var locationElement) ||
            !locationElement.TryGetProperty("solar_system_name", out var solarSystemNameElement) ||
            solarSystemNameElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var externalId = externalIdElement.GetString();
        var solarSystemName = solarSystemNameElement.GetString();
        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(solarSystemName))
        {
            return LandingPageUrl;
        }

        var reportDate = TryGetReportDate(document.RootElement, periodStart.UtcDateTime);
        var slug = Uri.EscapeDataString(solarSystemName.Trim());
        return $"https://killmail.app/br/{externalId}-{slug}-{reportDate}";
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HISA/1.0");
        return client;
    }

    private static string TryGetReportDate(JsonElement rootElement, DateTime fallbackUtc)
    {
        if (rootElement.TryGetProperty("period_start", out var periodStartElement) &&
            periodStartElement.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(periodStartElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedPeriodStart))
        {
            return parsedPeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return fallbackUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private sealed class BattleReportRequest
    {
        [JsonPropertyName("solar_system_id")]
        public required long SolarSystemId { get; init; }

        [JsonPropertyName("period_start")]
        public required DateTime PeriodStart { get; init; }

        [JsonPropertyName("period_end")]
        public required DateTime PeriodEnd { get; init; }

        [JsonPropertyName("side_overrides")]
        public required int[] SideOverrides { get; init; }
    }
}
