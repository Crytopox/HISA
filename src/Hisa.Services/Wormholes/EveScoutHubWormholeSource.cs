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
            var maxJumpMassKg = TryReadLong(item, "max_jump_mass", "maxJumpMass", "max_mass", "jump_mass");
            var maxStableMassKg = TryReadLong(item, "max_stable_mass", "maxStableMass", "max_total_mass", "stable_mass");

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

            DateTimeOffset? reportedAtUtc = null;
            if (item.TryGetProperty("created_at", out var createdElement) &&
                DateTimeOffset.TryParse(createdElement.GetString(), out var parsedCreated))
            {
                reportedAtUtc = parsedCreated;
            }
            else if (item.TryGetProperty("reported_at", out var reportedElement) &&
                     DateTimeOffset.TryParse(reportedElement.GetString(), out var parsedReported))
            {
                reportedAtUtc = parsedReported;
            }

            DateTimeOffset? lastUpdatedAtUtc = null;
            if (item.TryGetProperty("updated_at", out var updatedElement) &&
                DateTimeOffset.TryParse(updatedElement.GetString(), out var parsedUpdated))
            {
                lastUpdatedAtUtc = parsedUpdated;
            }
            else if (item.TryGetProperty("last_modified", out var lastModifiedElement) &&
                     DateTimeOffset.TryParse(lastModifiedElement.GetString(), out var parsedLastModified))
            {
                lastUpdatedAtUtc = parsedLastModified;
            }

            result.Add(new HubWormholeConnection
            {
                SolarSystemId = targetSystemId.Value,
                HubType = hubType,
                ExpiresAtUtc = expiresAtUtc,
                ReportedAtUtc = reportedAtUtc,
                LastUpdatedAtUtc = lastUpdatedAtUtc,
                OutSignature = outSignature,
                InSignature = inSignature,
                MaxShipSize = maxShipSize,
                MaxJumpMassKg = maxJumpMassKg,
                MaxStableMassKg = maxStableMassKg
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

    private static long? TryReadLong(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!item.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt64(out var numeric))
                {
                    return numeric;
                }

                if (value.TryGetDouble(out var asDouble) && !double.IsNaN(asDouble) && !double.IsInfinity(asDouble))
                {
                    return (long)Math.Round(asDouble);
                }
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var raw = value.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var cleaned = raw.Trim().Replace(",", string.Empty);
                if (long.TryParse(cleaned, out var parsedLong))
                {
                    return parsedLong;
                }

                if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble) &&
                    !double.IsNaN(parsedDouble) &&
                    !double.IsInfinity(parsedDouble))
                {
                    return (long)Math.Round(parsedDouble);
                }
            }
        }

        return null;
    }
}
