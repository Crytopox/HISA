using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Esi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hisa.Esi.Auth;

internal sealed class EsiAuthService : IEsiAuthService
{
    private const string TokenSettingsKey = "Esi.Auth.Token.v1";
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly IOptionsMonitor<EsiOptions> _options;
    private readonly ILogger<EsiAuthService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _pendingCodeVerifiers = [];

    public EsiAuthService(
        HttpClient httpClient,
        ISettingsService settingsService,
        IOptionsMonitor<EsiOptions> options,
        ILogger<EsiAuthService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _options = options;
        _logger = logger;
    }

    public async Task<EsiAuthStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var configured = IsConfigured(_options.CurrentValue.OAuth);
        var token = await _settingsService.GetAsync<EsiStoredToken>(TokenSettingsKey, cancellationToken);
        if (!configured || token is null)
        {
            return new EsiAuthStatus
            {
                IsConfigured = configured,
                IsAuthenticated = false,
                ExpiresAtUtc = null,
                Scopes = [],
                CharacterId = null,
                CharacterName = null
            };
        }

        var claims = ParseClaims(token.AccessToken);
        return new EsiAuthStatus
        {
            IsConfigured = true,
            IsAuthenticated = token.ExpiresAtUtc > DateTimeOffset.UtcNow,
            ExpiresAtUtc = token.ExpiresAtUtc,
            Scopes = token.Scopes,
            CharacterId = claims.CharacterId,
            CharacterName = claims.CharacterName
        };
    }

    public Task ClearTokenAsync(CancellationToken cancellationToken = default)
    {
        return _settingsService.SetAsync<EsiStoredToken?>(TokenSettingsKey, null, cancellationToken);
    }

    public Task<Uri?> BeginAuthorizationAsync(IEnumerable<string>? scopes = null, string? state = null, CancellationToken cancellationToken = default)
    {
        var o = _options.CurrentValue.OAuth;
        if (!IsConfigured(o))
        {
            return Task.FromResult<Uri?>(null);
        }

        var authorizationState = string.IsNullOrWhiteSpace(state) ? CreateRandomUrlSafe(24) : state.Trim();
        var verifier = CreateRandomUrlSafe(48);
        var challenge = CreatePkceChallenge(verifier);
        _pendingCodeVerifiers[authorizationState] = verifier;

        var scopeList = (scopes ?? o.DefaultScopes)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var scopeString = string.Join(' ', scopeList);
        var uri = $"{o.AuthorizationEndpoint}?response_type=code&client_id={Uri.EscapeDataString(o.ClientId)}&redirect_uri={Uri.EscapeDataString(o.CallbackUrl)}&scope={Uri.EscapeDataString(scopeString)}&state={Uri.EscapeDataString(authorizationState)}&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256";
        return Task.FromResult<Uri?>(new Uri(uri));
    }

    public async Task<bool> CompleteAuthorizationAsync(string code, string state, CancellationToken cancellationToken = default)
    {
        var o = _options.CurrentValue.OAuth;
        if (!IsConfigured(o) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        if (!_pendingCodeVerifiers.TryGetValue(state, out var verifier))
        {
            _logger.LogWarning("Esi OAuth callback rejected: unknown state.");
            return false;
        }

        _pendingCodeVerifiers.Remove(state);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var payload = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = o.ClientId,
                ["redirect_uri"] = o.CallbackUrl,
                ["code_verifier"] = verifier
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, o.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(payload)
            };
            req.Headers.Authorization = BuildBasicAuth(o.ClientId, o.ClientSecret);

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Esi OAuth token exchange failed: {Status}", (int)res.StatusCode);
                return false;
            }

            await SaveTokenResponseAsync(res, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var o = _options.CurrentValue.OAuth;
        if (!IsConfigured(o))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var token = await _settingsService.GetAsync<EsiStoredToken>(TokenSettingsKey, cancellationToken);
            if (token is null)
            {
                return null;
            }

            if (token.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return token.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                return null;
            }

            var payload = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = token.RefreshToken,
                ["client_id"] = o.ClientId
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, o.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(payload)
            };
            req.Headers.Authorization = BuildBasicAuth(o.ClientId, o.ClientSecret);

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Esi OAuth refresh failed: {Status}", (int)res.StatusCode);
                return null;
            }

            var saved = await SaveTokenResponseAsync(res, cancellationToken);
            return saved.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<EsiStoredToken> SaveTokenResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<EsiTokenResponse>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Invalid ESI token payload.");

        var scopes = string.IsNullOrWhiteSpace(payload.Scope)
            ? []
            : payload.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var token = new EsiStoredToken
        {
            AccessToken = payload.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(payload.RefreshToken) ? string.Empty : payload.RefreshToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, payload.ExpiresInSeconds)),
            Scopes = scopes
        };

        await _settingsService.SetAsync(TokenSettingsKey, token, cancellationToken);
        return token;
    }

    private static System.Net.Http.Headers.AuthenticationHeaderValue BuildBasicAuth(string clientId, string clientSecret)
    {
        var bytes = Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
        return new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    private static bool IsConfigured(EsiOAuthOptions options)
    {
        if (!options.Enabled)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(options.ClientId)
            && !string.IsNullOrWhiteSpace(options.ClientSecret)
            && !string.IsNullOrWhiteSpace(options.CallbackUrl);
    }

    private static string CreateRandomUrlSafe(int bytes)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string CreatePkceChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static (long? CharacterId, string? CharacterName) ParseClaims(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return (null, null);
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            long? characterId = null;
            if (root.TryGetProperty("sub", out var subElement))
            {
                var sub = subElement.GetString();
                var suffix = sub?.Split(':').LastOrDefault();
                if (long.TryParse(suffix, out var parsedId))
                {
                    characterId = parsedId;
                }
            }

            string? name = null;
            if (root.TryGetProperty("name", out var nameElement))
            {
                name = nameElement.GetString();
            }

            return (characterId, name);
        }
        catch
        {
            return (null, null);
        }
    }
}
