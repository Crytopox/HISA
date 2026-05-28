using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IEsiAuthService
{
    Task<EsiAuthStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task<Uri?> BeginAuthorizationAsync(IEnumerable<string>? scopes = null, string? state = null, CancellationToken cancellationToken = default);
    Task<bool> CompleteAuthorizationAsync(string code, string state, CancellationToken cancellationToken = default);
    Task ClearTokenAsync(CancellationToken cancellationToken = default);
}
