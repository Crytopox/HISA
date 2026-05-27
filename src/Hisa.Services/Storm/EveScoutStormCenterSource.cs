using System.Text.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Storm;

public sealed class EveScoutStormCenterSource : IStormCenterSource
{
    private const string ObservationsPath = "/v2/public/observations";
    private readonly HttpClient _httpClient;
    private readonly ILogger<EveScoutStormCenterSource> _logger;

    public EveScoutStormCenterSource(HttpClient httpClient, ILogger<EveScoutStormCenterSource> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StormCenter>> GetStormCentersAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(ObservationsPath, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var centers = new Dictionary<long, StormCenter>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("system_id", out var systemIdElement) || !systemIdElement.TryGetInt64(out var systemId))
            {
                continue;
            }

            var observationType = item.TryGetProperty("observation_type", out var typeElement)
                ? typeElement.GetString()
                : null;
            var mappedType = MapObservationType(observationType);
            if (mappedType == StormType.Unknown)
            {
                continue;
            }

            var displayName = item.TryGetProperty("display_name", out var displayNameElement)
                ? displayNameElement.GetString()
                : null;
            DateTimeOffset? createdAt = null;
            if (item.TryGetProperty("created_at", out var createdAtElement)
                && DateTimeOffset.TryParse(createdAtElement.GetString(), out var parsedCreatedAt))
            {
                createdAt = parsedCreatedAt;
            }

            centers[systemId] = new StormCenter
            {
                SolarSystemId = systemId,
                Type = mappedType,
                DisplayName = displayName,
                ReportedAtUtc = createdAt
            };
        }

        _logger.LogInformation("Loaded {Count} storm centers from EvE-Scout observations.", centers.Count);
        return centers.Values.ToList();
    }

    private static StormType MapObservationType(string? observationType)
    {
        return observationType?.Trim().ToLowerInvariant() switch
        {
            "electric_a" => StormType.Electrical,
            "electric_b" => StormType.Electrical,
            "gamma_a" => StormType.Gamma,
            "gamma_b" => StormType.Gamma,
            "exotic_a" => StormType.Exotic,
            "exotic_b" => StormType.Exotic,
            "plasma_a" => StormType.Plasma,
            "plasma_b" => StormType.Plasma,
            _ => StormType.Unknown
        };
    }
}
