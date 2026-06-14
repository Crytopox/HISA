using System.Diagnostics;
using System.Text;
using Hisa.App.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hisa.App;

public interface IAppLogFileService
{
    string LogsDirectoryPath { get; }
    Task<string> ExportSnapshotAsync(IReadOnlyList<AppLogEntry> entries, CancellationToken cancellationToken = default);
    void OpenLogsFolder();
}

public sealed class AppLogFileService : IAppLogFileService, IDisposable
{
    private readonly object _sync = new();
    private readonly AppLogStore _store;
    private readonly string _logsDirectory;
    private readonly LogLevel _persistedFileLogLevel;

    public AppLogFileService(AppLogStore store, IConfiguration configuration)
    {
        _store = store;
        _logsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hisa", "logs");
        _persistedFileLogLevel = ResolvePersistedFileLogLevel(configuration);
        Directory.CreateDirectory(_logsDirectory);
        _store.EntryAdded += OnEntryAdded;
    }

    public string LogsDirectoryPath => _logsDirectory;

    public async Task<string> ExportSnapshotAsync(IReadOnlyList<AppLogEntry> entries, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_logsDirectory);
        var fileName = $"hisa-debug-export-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log";
        var path = Path.Combine(_logsDirectory, fileName);
        var sb = new StringBuilder(entries.Count * 80);
        foreach (var e in entries)
        {
            sb.Append(FormatLine(e));
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken);
        return path;
    }

    public void OpenLogsFolder()
    {
        Directory.CreateDirectory(_logsDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _logsDirectory,
            UseShellExecute = true
        });
    }

    private void OnEntryAdded(object? sender, AppLogEntry entry)
    {
        if (entry.Level < _persistedFileLogLevel)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_logsDirectory);
            var dayFile = Path.Combine(_logsDirectory, $"hisa-{DateTimeOffset.Now:yyyyMMdd}.log");
            var latestFile = Path.Combine(_logsDirectory, "hisa-latest.log");
            var line = FormatLine(entry) + Environment.NewLine;
            lock (_sync)
            {
                File.AppendAllText(dayFile, line, Encoding.UTF8);
                File.AppendAllText(latestFile, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Never throw from log sink.
        }
    }

    private static string FormatLine(AppLogEntry e)
    {
        var ex = string.IsNullOrWhiteSpace(e.Exception) ? string.Empty : $" | {e.Exception}";
        return $"{e.TimestampUtc:O} [{e.Level}] [{e.SourceTag}] {e.Category}: {e.Message}{ex}";
    }

    public void Dispose()
    {
        _store.EntryAdded -= OnEntryAdded;
    }

    private static LogLevel ResolvePersistedFileLogLevel(IConfiguration configuration)
    {
        var configured = configuration["Hisa:Diagnostics:PersistedFileLogLevel"];
        if (!string.IsNullOrWhiteSpace(configured) && Enum.TryParse<LogLevel>(configured, true, out var parsed))
        {
            return parsed < LogLevel.Warning ? LogLevel.Warning : parsed;
        }

        return LogLevel.Warning;
    }
}
