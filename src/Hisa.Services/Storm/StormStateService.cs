using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Storm;

public sealed class StormStateService : IStormStateService
{
    private readonly IStormCenterSource _stormCenterSource;
    private readonly ISdeDatabase _sdeDatabase;
    private readonly ILogger<StormStateService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Dictionary<long, List<long>>? _neighborsBySystemId;

    public StormStateService(
        IStormCenterSource stormCenterSource,
        ISdeDatabase sdeDatabase,
        ILogger<StormStateService> logger)
    {
        _stormCenterSource = stormCenterSource;
        _sdeDatabase = sdeDatabase;
        _logger = logger;
    }

    public StormSnapshot Current { get; private set; } = StormSnapshot.Empty;
    public event EventHandler<StormSnapshot>? StormSnapshotUpdated;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var centers = await _stormCenterSource.GetStormCentersAsync(cancellationToken);
            var neighbors = await GetNeighborsAsync(cancellationToken);
            var effectsBySystemId = BuildEffects(centers, neighbors);

            Current = new StormSnapshot
            {
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Source = "eve-scout-api",
                Centers = centers,
                EffectsBySystemId = effectsBySystemId
            };
            StormSnapshotUpdated?.Invoke(this, Current);

            _logger.LogInformation(
                "Storm snapshot updated. Centers: {CenterCount}, affected systems: {AffectedSystems}.",
                centers.Count,
                effectsBySystemId.Count);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<Dictionary<long, List<long>>> GetNeighborsAsync(CancellationToken cancellationToken)
    {
        if (_neighborsBySystemId is not null)
        {
            return _neighborsBySystemId;
        }

        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fromSolarSystemID, toSolarSystemID
            FROM mapSolarSystemJumps;
            """;

        var neighbors = new Dictionary<long, List<long>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var from = reader.GetInt64(0);
            var to = reader.GetInt64(1);

            if (!neighbors.TryGetValue(from, out var fromNeighbors))
            {
                fromNeighbors = [];
                neighbors[from] = fromNeighbors;
            }
            fromNeighbors.Add(to);

            if (!neighbors.TryGetValue(to, out var toNeighbors))
            {
                toNeighbors = [];
                neighbors[to] = toNeighbors;
            }
            toNeighbors.Add(from);
        }

        _neighborsBySystemId = neighbors;
        return neighbors;
    }

    private static IReadOnlyDictionary<long, IReadOnlyList<StormEffect>> BuildEffects(
        IReadOnlyList<StormCenter> centers,
        IReadOnlyDictionary<long, List<long>> neighbors)
    {
        var effects = new Dictionary<long, List<StormEffect>>();
        foreach (var center in centers)
        {
            var distances = ComputeDistances(center.SolarSystemId, 3, neighbors);
            foreach (var (systemId, distance) in distances)
            {
                var strength = distance switch
                {
                    0 => StormStrength.Center,
                    1 => StormStrength.Strong,
                    2 or 3 => StormStrength.Weak,
                    _ => StormStrength.Weak
                };

                if (!effects.TryGetValue(systemId, out var list))
                {
                    list = [];
                    effects[systemId] = list;
                }

                var alreadyExists = list.Any(e =>
                    e.CenterSolarSystemId == center.SolarSystemId &&
                    e.Type == center.Type &&
                    e.Strength == strength);
                if (!alreadyExists)
                {
                    list.Add(new StormEffect
                    {
                        CenterSolarSystemId = center.SolarSystemId,
                        Type = center.Type,
                        Strength = strength
                    });
                }
            }
        }

        return effects.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<StormEffect>)kvp.Value
                .OrderByDescending(e => e.Strength)
                .ThenBy(e => e.Type)
                .ToList());
    }

    private static Dictionary<long, int> ComputeDistances(
        long centerSystemId,
        int maxDistance,
        IReadOnlyDictionary<long, List<long>> neighbors)
    {
        var distances = new Dictionary<long, int> { [centerSystemId] = 0 };
        var queue = new Queue<long>();
        queue.Enqueue(centerSystemId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentDistance = distances[current];
            if (currentDistance >= maxDistance)
            {
                continue;
            }

            if (!neighbors.TryGetValue(current, out var nextSystems))
            {
                continue;
            }

            foreach (var next in nextSystems)
            {
                if (distances.ContainsKey(next))
                {
                    continue;
                }

                distances[next] = currentDistance + 1;
                queue.Enqueue(next);
            }
        }

        return distances;
    }
}
