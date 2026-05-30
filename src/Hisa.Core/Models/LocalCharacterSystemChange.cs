namespace Hisa.Core.Models;

public sealed class LocalCharacterSystemChange
{
    public required int CharacterId { get; init; }
    public required string CharacterName { get; init; }
    public required string SolarSystemName { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required string SourceFilePath { get; init; }
}
