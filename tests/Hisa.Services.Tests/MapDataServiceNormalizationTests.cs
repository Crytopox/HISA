using Hisa.Core.Models;
using Hisa.Services;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace Hisa.Services.Tests;

public sealed class MapDataServiceNormalizationTests(ITestOutputHelper output)
{
    [Fact]
    public void UniverseNormalizationPreservesSdeAxisScaleAndPositions()
    {
        var raw = new[]
        {
            new MapNode { Id = 1, Name = "A", X = 100, Y = 200 },
            new MapNode { Id = 2, Name = "B", X = 500, Y = 200 },
            new MapNode { Id = 3, Name = "C", X = 300, Y = 400 }
        };

        var graph = MapDataService.BuildNormalizedGraph(raw, [], preserveAspect: true);

        Assert.Equal((0.0, 0.75), (graph.Nodes[0].X, graph.Nodes[0].Y));
        Assert.Equal((1.0, 0.75), (graph.Nodes[1].X, graph.Nodes[1].Y));
        Assert.Equal((0.5, 0.25), (graph.Nodes[2].X, graph.Nodes[2].Y));
        Assert.Equal(0.5, graph.Nodes[2].X - graph.Nodes[0].X, 9);
        Assert.Equal(0.5, graph.Nodes[0].Y - graph.Nodes[2].Y, 9);
    }

    [Fact]
    public void RegionNormalizationKeepsIndependentAxisScaling()
    {
        var raw = new[]
        {
            new MapNode { Id = 1, Name = "A", X = 100, Y = 200 },
            new MapNode { Id = 2, Name = "B", X = 500, Y = 400 }
        };

        var graph = MapDataService.BuildNormalizedGraph(raw, []);

        Assert.Equal((0.0, 1.0), (graph.Nodes[0].X, graph.Nodes[0].Y));
        Assert.Equal((1.0, 0.0), (graph.Nodes[1].X, graph.Nodes[1].Y));
    }

    [Fact]
    public void FlattenedUniverseSeparatesClosePairsWithinMovementLimit()
    {
        var raw = new[]
        {
            new MapNode { Id = 1, Name = "Left", X = 0, Y = 0 },
            new MapNode { Id = 2, Name = "Right", X = 1, Y = 1 },
            new MapNode { Id = 3, Name = "Near A", X = 0.5, Y = 0.5 },
            new MapNode { Id = 4, Name = "Near B", X = 0.5002, Y = 0.5 }
        };
        var links = new[] { new MapLink { FromId = 3, ToId = 4 } };

        var projected = MapDataService.BuildNormalizedGraph(raw, links, preserveAspect: true);
        var spaced = MapDataService.BuildNormalizedGraph(raw, links, preserveAspect: true,
            applyFlattenedUniverseDeoverlap: true);

        Assert.True(Distance(spaced.Nodes[2], spaced.Nodes[3]) >
                    Distance(projected.Nodes[2], projected.Nodes[3]));
        Assert.True(Distance(spaced.Nodes[2], spaced.Nodes[3]) >= 0.0022);
        Assert.All(spaced.Nodes.Zip(projected.Nodes), pair =>
            Assert.InRange(Distance(pair.First, pair.Second), 0, 0.0015 + 1e-12));
        Assert.Equal(links[0].FromId, spaced.Links[0].FromId);
        Assert.Equal(links[0].ToId, spaced.Links[0].ToId);
        Assert.Equal(projected.Nodes.Select(node => (node.X, node.Y)),
            MapDataService.BuildNormalizedGraph(raw, links, preserveAspect: true)
                .Nodes.Select(node => (node.X, node.Y)));
    }

    [Fact]
    public void DenseAndCoincidentNodesUseStableIdBasedPlacement()
    {
        var raw = new[]
        {
            new MapNode { Id = 90, Name = "A", X = 0.5, Y = 0.5 },
            new MapNode { Id = 7, Name = "B", X = 0.5, Y = 0.5 },
            new MapNode { Id = 12, Name = "C", X = 0.5001, Y = 0.5 },
            new MapNode { Id = 3, Name = "D", X = 0.5, Y = 0.5001 },
            new MapNode { Id = 1, Name = "Min", X = 0, Y = 0 },
            new MapNode { Id = 2, Name = "Max", X = 1, Y = 1 }
        };

        var original = MapDataService.BuildNormalizedGraph(raw, [], preserveAspect: true);
        var first = MapDataService.BuildNormalizedGraph(raw, [], preserveAspect: true,
            applyFlattenedUniverseDeoverlap: true);
        var reversed = MapDataService.BuildNormalizedGraph(raw.Reverse().ToArray(), [], preserveAspect: true,
            applyFlattenedUniverseDeoverlap: true);
        var byId = reversed.Nodes.ToDictionary(node => node.Id);

        Assert.True(Distance(first.Nodes[0], first.Nodes[1]) > 0);
        Assert.True(Distance(first.Nodes[0], first.Nodes[1]) > Distance(original.Nodes[0], original.Nodes[1]));
        foreach (var node in first.Nodes)
        {
            Assert.Equal(node.X, byId[node.Id].X, 12);
            Assert.Equal(node.Y, byId[node.Id].Y, 12);
            var source = original.Nodes.Single(item => item.Id == node.Id);
            Assert.InRange(Distance(node, source), 0, 0.0015 + 1e-12);
        }
    }

    [Fact]
    public void BundledFlattenedUniverseHasFarFewerTouchingNodes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "src", "Hisa.App", "Data", "eve-hk-sde.db")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        var databasePath = Path.Combine(directory.FullName, "src", "Hisa.App", "Data", "eve-hk-sde.db");
        var builder = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT solarSystemID, solarSystemName, x, z
            FROM mapSolarSystems
            WHERE x IS NOT NULL AND z IS NOT NULL
              AND solarSystemID IN (
                  SELECT fromSolarSystemID FROM mapSolarSystemJumps
                  UNION SELECT toSolarSystemID FROM mapSolarSystemJumps);
            """;
        var raw = new List<MapNode>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                raw.Add(new MapNode
                {
                    Id = reader.GetInt64(0), Name = reader.GetString(1),
                    X = reader.GetDouble(2), Y = reader.GetDouble(3)
                });
            }
        }

        var projected = MapDataService.BuildNormalizedGraph(raw, [], preserveAspect: true);
        var spaced = MapDataService.BuildNormalizedGraph(raw, [], preserveAspect: true,
            applyFlattenedUniverseDeoverlap: true);
        var repeated = MapDataService.BuildNormalizedGraph(raw, [], preserveAspect: true,
            applyFlattenedUniverseDeoverlap: true);
        var before = CountNodesWithNearNeighbor(projected.Nodes, 0.0022);
        var after = CountNodesWithNearNeighbor(spaced.Nodes, 0.0022);
        output.WriteLine($"Bundled SDE systems: {raw.Count}; near nodes before: {before}; after: {after}");

        Assert.True(before > 1000, $"Expected crowded SDE projection; found {before} near nodes.");
        Assert.True(after < before * 0.05, $"Near nodes: {before} before, {after} after.");
        Assert.All(spaced.Nodes.Zip(projected.Nodes), pair =>
            Assert.InRange(Distance(pair.First, pair.Second), 0, 0.0015 + 1e-12));
        Assert.Equal(spaced.Nodes.Select(node => (node.Id, node.X, node.Y)),
            repeated.Nodes.Select(node => (node.Id, node.X, node.Y)));
    }

    private static double Distance(MapNode a, MapNode b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static int CountNodesWithNearNeighbor(IReadOnlyList<MapNode> nodes, double threshold)
    {
        var thresholdSquared = threshold * threshold;
        var count = 0;
        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = 0; j < nodes.Count; j++)
            {
                if (i == j) continue;
                var dx = nodes[i].X - nodes[j].X;
                var dy = nodes[i].Y - nodes[j].Y;
                if ((dx * dx) + (dy * dy) >= thresholdSquared) continue;
                count++;
                break;
            }
        }
        return count;
    }
}
