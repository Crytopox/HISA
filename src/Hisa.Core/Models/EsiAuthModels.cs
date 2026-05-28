namespace Hisa.Core.Models;

public sealed class EsiAuthStatus
{
    public required bool IsConfigured { get; init; }
    public required bool IsAuthenticated { get; init; }
    public required DateTimeOffset? ExpiresAtUtc { get; init; }
    public required string[] Scopes { get; init; }
    public required long? CharacterId { get; init; }
    public required string? CharacterName { get; init; }
}
