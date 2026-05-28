using Hisa.Core.Abstractions;

namespace Hisa.Esi.Clients;

public interface IEsiAccessTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

internal sealed class EsiAccessTokenProvider : IEsiAccessTokenProvider
{
    private readonly IEsiAuthService _authService;

    public EsiAccessTokenProvider(IEsiAuthService authService)
    {
        _authService = authService;
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return _authService.GetAccessTokenAsync(cancellationToken);
    }
}
