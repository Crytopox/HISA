using System.ComponentModel;
using Avalonia.Threading;
using Hisa.Core;
using Hisa.Core.Abstractions;
using Hisa.Data.Database;

namespace Hisa.App;

public sealed partial class MainWindowViewModel
{
    public HistoricalActivityFeature History { get; private set; } = null!;

    private void InitializeHistoricalActivity(IIntelligenceActivityRepository repository, ISdeDatabase sde, TimeSpan retention)
    {
        History = new HistoricalActivityFeature(repository, _settingsService,
            async action => await Dispatcher.UIThread.InvokeAsync(action), retention,
            (ids, token) => Task.Run(async () =>
            {
                var names = new Dictionary<int, string>();
                if (ids.Count == 0) return (IReadOnlyDictionary<int, string>)names;
                await using var connection = sde.CreateConnection();
                await connection.OpenAsync(token);
                using var command = connection.CreateCommand();
                var parameters = ids.Distinct().Take(8).Select((id, index) =>
                {
                    var name = "$id" + index; command.Parameters.AddWithValue(name, id); return name;
                }).ToArray();
                command.CommandText = $"SELECT typeID, typeName FROM invTypes WHERE typeID IN ({string.Join(',', parameters)});";
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token)) names[reader.GetInt32(0)] = reader.GetString(1);
                return (IReadOnlyDictionary<int, string>)names;
            }, token));
        PropertyChanged += OnHistoricalMapContextChanged;
    }

    private void OnHistoricalMapContextChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(CurrentGraph) or nameof(SelectedViewMode) or nameof(SelectedNodeId))
            History.SetMapContext(CurrentGraph, SelectedViewMode, SelectedNodeId);
    }

    private void StopHistoricalActivity()
    {
        PropertyChanged -= OnHistoricalMapContextChanged;
        _ = History.DisposeAsync();
    }
}
