using System.Text.Json.Serialization;

namespace Hisa.Esi.Auth;

internal sealed class EsiTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; init; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresInSeconds { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}

internal sealed class EsiStoredToken
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public required string[] Scopes { get; init; }
}
