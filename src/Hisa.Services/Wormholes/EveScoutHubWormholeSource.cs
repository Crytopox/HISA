using System.Text.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Wormholes;

public sealed class EveScoutHubWormholeSource : IHubWormholeSource
{
    private const string SignaturesPath = "/v2/public/signatures";
    private readonly HttpClient _httpClient;
    private readonly ILogger<EveScoutHubWormholeSource> _logger;

    public EveScoutHubWormholeSource(HttpClient httpClient, ILogger<EveScoutHubWormholeSource> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HubWormholeConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(SignaturesPath, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<HubWormholeConnection>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var outSystemId = item.TryGetProperty("out_system_id", out var outIdElement) && outIdElement.TryGetInt64(out var parsedOutId)
                ? parsedOutId
                : (long?)null;
            var inSystemId = item.TryGetProperty("in_system_id", out var inIdElement) && inIdElement.TryGetInt64(out var parsedInId)
                ? parsedInId
                : (long?)null;

            var outSystemName = item.TryGetProperty("out_system_name", out var outNameElement) ? outNameElement.GetString() : null;
            var inSystemName = item.TryGetProperty("in_system_name", out var inNameElement) ? inNameElement.GetString() : null;
            var outSignature = item.TryGetProperty("out_signature", out var outSignatureElement) ? outSignatureElement.GetString() : null;
            var inSignature = item.TryGetProperty("in_signature", out var inSignatureElement) ? inSignatureElement.GetString() : null;
            var maxShipSize = item.TryGetProperty("max_ship_size", out var maxShipSizeElement) ? maxShipSizeElement.GetString() : null;

            var hubType = GetHubType(outSystemName) is var outHub && outHub != WormholeHubType.Unknown
                ? outHub
                : GetHubType(inSystemName);
            if (hubType == WormholeHubType.Unknown)
            {
                continue;
            }

            long? targetSystemId = null;
            if (GetHubType(outSystemName) != WormholeHubType.Unknown)
            {
                targetSystemId = inSystemId;
            }
            else if (GetHubType(inSystemName) != WormholeHubType.Unknown)
            {
                targetSystemId = outSystemId;
            }

            if (targetSystemId is null)
            {
                continue;
            }

            DateTimeOffset? expiresAtUtc = null;
            if (item.TryGetProperty("expires_at", out var expiresElement) &&
                DateTimeOffset.TryParse(expiresElement.GetString(), out var parsedExpires))
            {
                expiresAtUtc = parsedExpires;
            }

            result.Add(new HubWormholeConnection
            {
                SolarSystemId = targetSystemId.Value,
                HubType = hubType,
                ExpiresAtUtc = expiresAtUtc,
                OutSignature = outSignature,
                InSignature = inSignature,
                MaxShipSize = maxShipSize
            });
        }

        _logger.LogInformation("Loaded {Count} Thera/Turnur wormhole connections from EvE-Scout.", result.Count);
        return result;
    }

    private static WormholeHubType GetHubType(string? systemName)
    {
        if (string.Equals(systemName, "Thera", StringComparison.OrdinalIgnoreCase))
        {
            return WormholeHubType.Thera;
        }

        if (string.Equals(systemName, "Turnur", StringComparison.OrdinalIgnoreCase))
        {
            return WormholeHubType.Turnur;
        }

        return WormholeHubType.Unknown;
    }
}
