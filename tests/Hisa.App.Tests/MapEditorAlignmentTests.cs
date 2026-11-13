using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hisa.App;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Rendering.Controls;
using Hisa.Services;
using Microsoft.Data.Sqlite;

namespace Hisa.App.Tests;

public sealed class MapEditorAlignmentTests
{
    [AvaloniaFact]
    public async Task AlignSelectedNodes_SnapsLegacyCoordinatesAndPersistsOnSave()
    {
        var connectionString = $"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared";
        await using var database = new SqliteConnection(connectionString);
        await database.OpenAsync();
        await new SqliteDatabaseInitializer(connectionString).InitializeAsync();
        var sdeDatabase = new SdeSqliteDatabase(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(AppContext.BaseDirectory, "Data", "eve-hk-sde.db"),
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        var layouts = new SqliteMapLayoutDataService(connectionString);
        var editor = new SqliteMapLayoutEditorService(connectionString, sdeDatabase);
        var mapData = new MapDataService(sdeDatabase, layouts, null!, null!, null!, null!, null!, null!);
        var region = await editor.CreateCustomRegionAsync("Legacy layout");
        await editor.SaveLayoutRegionGraphAsync(region.Id, new MapGraph
        {
            Nodes =
            [
                new MapNode { Id = 30000605, Name = "L-Z9KJ", X = 0.240, Y = 0.432 },
                new MapNode { Id = 30000606, Name = "7K-NSE", X = 0.264, Y = 0.432 },
                new MapNode { Id = 30000570, Name = "G9NE-B", X = -0.208, Y = 0.456 }
            ],
            Links = []
        });

        var vm = new MapEditorViewModel(mapData, layouts, editor);
        await vm.InitialLoadTask;
        var window = new MapEditorWindow(vm);
        window.Show();
        try
        {
            var alignButton = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Align to Grid"));
            var map = window.FindControl<MapControl>("EditorMapControl")!;
            map.PanBy(31, -12);
            map.ZoomBy(1.2);
            var viewport = map.GetViewportState();
            var originalLinks = vm.CurrentGraph.Links;
            Assert.NotEmpty(originalLinks);
            Assert.Equal((0.264, 0.432), Position(vm.CurrentGraph, 30000606));
            Assert.False(alignButton.IsEnabled);

            vm.SetSelectedNodes([30000606, 30000570]);
            Assert.True(alignButton.IsEnabled);
            alignButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Aligned 2 nodes to the grid.", vm.StatusText);
            Assert.Equal((0.26, 0.43), Position(vm.CurrentGraph, 30000606));
            Assert.Equal((-0.21, 0.46), Position(vm.CurrentGraph, 30000570));
            Assert.Equal((0.24, 0.432), Position(vm.CurrentGraph, 30000605));
            Assert.Same(originalLinks, vm.CurrentGraph.Links);
            Assert.Equal(2, vm.GetSelectedNodeIds().Count);
            Assert.Equal(viewport.Zoom, map.GetViewportState().Zoom);
            Assert.Equal(viewport.PanOffsetX, map.GetViewportState().PanOffsetX);
            Assert.Equal(viewport.PanOffsetY, map.GetViewportState().PanOffsetY);
            Assert.Equal((0.264, 0.432), Position((await editor.GetLayoutRegionGraphAsync(region.Id))!, 30000606));

            var aligned = vm.CurrentGraph;
            vm.AlignSelectedNodesToGrid();
            Assert.Same(aligned, vm.CurrentGraph);
            Assert.Equal("Selected nodes are already aligned.", vm.StatusText);

            vm.SetSelectedNodes([30000606]);
            Assert.True(vm.MoveSelectedNodeAsync(0.003, 0.004, freeMove: true));
            Assert.Equal((0.263, 0.434), Position(vm.CurrentGraph, 30000606));
            Assert.True(vm.MoveSelectedNodeAsync(vm.EditorGridStep, 0));
            Assert.Equal((0.27, 0.43), Position(vm.CurrentGraph, 30000606));

            vm.SetSelectedNodes(vm.CurrentGraph.Nodes.Select(node => node.Id).ToArray());
            vm.AlignSelectedNodesToGrid();
            Assert.Equal("Aligned 1 node to the grid.", vm.StatusText);
            await vm.SaveCurrentRegionAsync();
            await vm.ReloadAsync();
            Assert.Equal((0.24, 0.43), Position(vm.CurrentGraph, 30000605));
            Assert.Equal((0.27, 0.43), Position(vm.CurrentGraph, 30000606));
            Assert.Equal((-0.21, 0.46), Position(vm.CurrentGraph, 30000570));
            Assert.Equal(originalLinks.Select(link => (link.FromId, link.ToId)),
                vm.CurrentGraph.Links.Select(link => (link.FromId, link.ToId)));

            var readOnly = (await layouts.GetLayoutRegionsAsync()).First(layout => layout.IsReadOnly);
            vm.SelectedLayoutRegion = readOnly;
            vm.SetSelectedNodes(vm.CurrentGraph.Nodes.Select(node => node.Id).ToArray());
            var readOnlyGraph = vm.CurrentGraph;
            vm.AlignSelectedNodesToGrid();
            Assert.False(alignButton.IsEnabled);
            Assert.Same(readOnlyGraph, vm.CurrentGraph);
        }
        finally
        {
            window.Close();
        }
    }

    private static (double X, double Y) Position(MapGraph graph, long id)
    {
        var node = graph.Nodes.Single(node => node.Id == id);
        return (Math.Round(node.X, 12), Math.Round(node.Y, 12));
    }
}
