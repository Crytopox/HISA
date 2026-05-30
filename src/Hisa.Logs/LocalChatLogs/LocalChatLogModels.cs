namespace Hisa.Logs.LocalChatLogs;

public sealed class LocalChatLogSession
{
    public required string Listener { get; init; }
    public required DateTime SessionStartedUtc { get; init; }
    public required IReadOnlyList<LocalSystemChange> SystemChanges { get; init; }
}

public sealed class LocalSystemChange
{
    public required DateTime TimestampUtc { get; init; }
    public required string SolarSystemName { get; init; }
}
