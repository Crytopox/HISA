using Avalonia.Headless.XUnit;
using Hisa.App;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Services;
using Hisa.Services.Background;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hisa.App.Tests;

public sealed class RegionSelectorTests
{
    private const string AmarrMap = "FW: Amarr VS Minmatar";
    private const string CaldariMap = "FW: Caldari VS Gallente";

    [AvaloniaFact]
    public async Task RegionSelectorHidesRegularRegionsAndLoadsFactionWarfareMaps()
    {
        await using var database = await OpenDatabaseAsync();
        await using var services = CreateServices(database.ConnectionString);
        await new SqliteDatabaseInitializer(database.ConnectionString).InitializeAsync();
        var settings = services.GetRequiredService<ISettingsService>();
        await settings.SetAsync("Map.SelectedViewMode", MapViewMode.Region);
        var layouts = services.GetRequiredService<IMapLayoutDataService>();
        var oldLayout = Assert.Single(await layouts.GetLayoutRegionsAsync(), region => region.Name == "A821-A");
        await settings.SetAsync("Map.SelectedRegionId", -2_000_000_000 + (int)oldLayout.Id);
        await settings.SetAsync("Map.SelectedRegionToken",
            new { RegionName = "A821-A", Kind = RegionOptionKind.Combined });
        await settings.SetAsync("Map.FavoriteRegionTokens", new[]
        {
            new { RegionName = "A821-A", Kind = RegionOptionKind.Regular }
        });

        var vm = services.GetRequiredService<MainWindowViewModel>();
        try
        {
            await vm.InitialLoadTask;
            Assert.Equal(10000019, vm.SelectedRegion?.RegionId);
            Assert.Null(vm.SelectedRegionInDropdown);
            Assert.NotEmpty(vm.CurrentGraph!.Nodes);
            Assert.DoesNotContain(vm.Regions, r => r.RegionName is "A821-A" or "J7HZ-F" or "UUA-F4");

            vm.RegionSearchText = "A821-A";
            Assert.Empty(vm.Regions);
            vm.RegionSearchText = "";
            var mapData = services.GetRequiredService<IMapDataService>();
            var universeRegions = await mapData.GetUniverseRegionsGraphAsync(MapCoordinateMode.SdePlanarXY);
            Assert.Contains(universeRegions.Nodes, node => node.Id == 10000017);
            await vm.OpenRegionFromUniverseRegionsNodeAsync(10000017);
            Assert.Equal(10000017, vm.SelectedRegion?.RegionId);
            Assert.Null(vm.SelectedRegionInDropdown);
            Assert.NotEmpty(vm.CurrentGraph!.Nodes);

            vm.MapSearchText = "A821-A";
            Assert.NotNull(await vm.ExecuteSearchAsync());
            Assert.Equal(10000019, vm.SelectedRegion?.RegionId);
            Assert.NotEmpty(vm.CurrentGraph!.Nodes);

            var editor = new MapEditorViewModel(mapData, layouts,
                new SqliteMapLayoutEditorService(database.ConnectionString, services.GetRequiredService<ISdeDatabase>()));
            await editor.InitialLoadTask;
            foreach (var id in new[] { RegionIds.A821A, RegionIds.J7HzF, RegionIds.UuaF4 })
            {
                Assert.Contains(editor.GameRegions, region => region.Kind == RegionOptionKind.Regular && region.RegionId == id);
            }

            var amarr = Assert.Single(vm.Regions, r => r.RegionName == AmarrMap);
            var caldari = Assert.Single(vm.Regions, r => r.RegionName == CaldariMap);
            Assert.Equal(RegionOptionKind.Combined, amarr.Kind);
            Assert.Equal(RegionOptionKind.Combined, caldari.Kind);

            var amarrGraph = await mapData.GetRegionGraphAsync(amarr.RegionId, MapCoordinateMode.SdePlanarXY);
            var caldariGraph = await mapData.GetRegionGraphAsync(caldari.RegionId, MapCoordinateMode.SdePlanarXY);
            AssertLayout(amarrGraph, 102, 151, 30000200, 0.9033915724563206, 0.1762589928057554);
            AssertLayout(caldariGraph, 144, 184, 30001376, 0.9044193216855088, 0.48458376156217886);
        }
        finally
        {
            vm.BeginApplicationShutdown();
        }
    }

    [AvaloniaFact]
    public async Task UuaF4IsHiddenInOverviewsAndCanStillOpenInRegionView()
    {
        await using var database = await OpenDatabaseAsync();
        await using var services = CreateServices(database.ConnectionString);
        await new SqliteDatabaseInitializer(database.ConnectionString).InitializeAsync();
        var mapData = services.GetRequiredService<IMapDataService>();

        foreach (var coordinateMode in Enum.GetValues<MapCoordinateMode>())
        {
            var universe = await mapData.GetUniverseGraphAsync(coordinateMode);
            Assert.NotEmpty(universe.Nodes);
            Assert.DoesNotContain(universe.Nodes, node => node.RegionId == RegionIds.UuaF4);

            var regions = await mapData.GetUniverseRegionsGraphAsync(coordinateMode);
            Assert.DoesNotContain(regions.Nodes, node => node.Id == RegionIds.UuaF4);
            Assert.Contains(regions.Nodes, node => node.Id == RegionIds.A821A);
            Assert.Contains(regions.Nodes, node => node.Id == RegionIds.J7HzF);
        }

        var vm = services.GetRequiredService<MainWindowViewModel>();
        try
        {
            await vm.InitialLoadTask;
            foreach (var viewMode in new[] { MapViewMode.Universe, MapViewMode.UniverseRegions })
            {
                vm.SelectedViewMode = viewMode;
                foreach (var term in new[] { "UUA-F4", "Polaris", "9RW5-Z" })
                {
                    var suggestionsUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                    {
                        if (args.PropertyName == nameof(vm.HasSearchSuggestions))
                        {
                            suggestionsUpdated.TrySetResult();
                        }
                    }

                    vm.PropertyChanged += OnPropertyChanged;
                    try
                    {
                        vm.MapSearchText = term;
                        await suggestionsUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        Assert.DoesNotContain(vm.SearchSuggestions, candidate => candidate.RegionId == RegionIds.UuaF4);
                    }
                    finally
                    {
                        vm.PropertyChanged -= OnPropertyChanged;
                    }

                    Assert.Null(await vm.ExecuteSearchAsync());
                    var candidate = Assert.Single(await mapData.SearchAsync(term), candidate => candidate.RegionId == RegionIds.UuaF4);
                    Assert.Null(await vm.ExecuteSearchAsync(candidate));
                }
            }

            vm.SelectedViewMode = MapViewMode.Region;
            vm.MapSearchText = "UUA-F4";
            Assert.NotNull(await vm.ExecuteSearchAsync());
            Assert.Equal(RegionIds.UuaF4, vm.SelectedRegion?.RegionId);
            Assert.Contains(vm.CurrentGraph!.Nodes, node => node.Name == "Polaris");
        }
        finally
        {
            vm.BeginApplicationShutdown();
        }
    }

    [Fact]
    public async Task StartupAddsMissingLayoutsWithoutReplacingExistingOrCustomRegions()
    {
        await using var database = await OpenDatabaseAsync();
        await using var services = CreateServices(database.ConnectionString);
        var initializer = new SqliteDatabaseInitializer(database.ConnectionString);
        await initializer.InitializeAsync();
        var layouts = services.GetRequiredService<IMapLayoutDataService>();
        var editor = new SqliteMapLayoutEditorService(database.ConnectionString, services.GetRequiredService<ISdeDatabase>());

        await using var delete = database.CreateCommand();
        delete.CommandText = "DELETE FROM MapLayoutRegion WHERE Name IN ($amarr, $caldari);";
        delete.Parameters.AddWithValue("$amarr", AmarrMap);
        delete.Parameters.AddWithValue("$caldari", CaldariMap);
        Assert.Equal(2, await delete.ExecuteNonQueryAsync());

        var custom = await editor.CreateCustomRegionAsync(CaldariMap);
        await editor.SaveLayoutRegionGraphAsync(custom.Id, new MapGraph
        {
            Nodes = [new MapNode { Id = 30000200, Name = "Akkio", X = 0.12, Y = 0.34 }],
            Links = []
        });
        var before = await layouts.GetLayoutRegionsAsync();

        await initializer.InitializeAsync();
        var firstSync = await layouts.GetLayoutRegionsAsync();
        await initializer.InitializeAsync();
        var secondSync = await layouts.GetLayoutRegionsAsync();

        Assert.Equal(before.Count + 1, firstSync.Count);
        Assert.Equal(firstSync.Select(region => region.Id), secondSync.Select(region => region.Id));
        Assert.All(before, region => Assert.Contains(secondSync, current =>
            current.Id == region.Id && current.Name == region.Name && current.IsReadOnly == region.IsReadOnly));
        Assert.True(Assert.Single(secondSync, region => region.Name == AmarrMap).IsReadOnly);
        Assert.False(Assert.Single(secondSync, region => region.Name == CaldariMap).IsReadOnly);
        var node = Assert.Single((await editor.GetLayoutRegionGraphAsync(custom.Id))!.Nodes);
        Assert.Equal(("Akkio", 0.12, 0.34), (node.Name, node.X, node.Y));

        await editor.RenameLayoutRegionAsync(custom.Id, "My Caldari layout");
        await initializer.InitializeAsync();
        var restored = await layouts.GetLayoutRegionsAsync();
        var caldari = Assert.Single(restored, region => region.Name == CaldariMap);
        Assert.True(caldari.IsReadOnly);
        Assert.Contains(restored, region => region.Id == custom.Id && region.Name == "My Caldari layout");
        var amarr = Assert.Single(restored, region => region.Name == AmarrMap);
        Assert.Equal(151, (await layouts.TryGetLayoutRegionGraphAsync(amarr.Id))!.Links.Count);
        Assert.Equal(184, (await layouts.TryGetLayoutRegionGraphAsync(caldari.Id))!.Links.Count);
    }

    private static void AssertLayout(MapGraph graph, int nodes, int links, long sampleId, double x, double y)
    {
        Assert.Equal(nodes, graph.Nodes.Count);
        Assert.Equal(nodes, graph.Nodes.Select(node => node.Id).Distinct().Count());
        Assert.Equal(links, graph.Links.Count);
        var sample = Assert.Single(graph.Nodes, node => node.Id == sampleId);
        Assert.Equal(x, sample.X, 10);
        Assert.Equal(y, sample.Y, 10);
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var database = new SqliteConnection($"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared;Foreign Keys=True");
        await database.OpenAsync();
        return database;
    }

    private static ServiceProvider CreateServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHisaServices(new ConfigurationBuilder().Build());
        services.AddHisaMapServices();
        services.AddSingleton<ISettingsService>(new SqliteSettingsService(connectionString));
        services.AddSingleton<IMapLayoutDataService>(new SqliteMapLayoutDataService(connectionString));
        services.AddSingleton<ISdeDatabase>(new SdeSqliteDatabase(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(AppContext.BaseDirectory, "Data", "eve-hk-sde.db"),
            Mode = SqliteOpenMode.ReadOnly
        }.ToString()));
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
