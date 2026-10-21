using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IEveSettingsIdentityService
{
    Task<IReadOnlyList<EveCharacterFile>> DiscoverCharactersAsync(IEnumerable<EveSettingsAccount> accounts, CancellationToken cancellationToken = default);
    IReadOnlyList<EveAccountGroup> GroupAccounts(IEnumerable<EveSettingsAccount> accounts);
    Task<IReadOnlyList<EveLocalCharacterLink>> LoadLinksAsync(CancellationToken cancellationToken = default);
    Task SaveLinksAsync(string groupKey, string server, IEnumerable<long> characterIds, CancellationToken cancellationToken = default);
}
public interface IEveCharacterResolver
{
    Task<EveCharacterIdentity> ResolveAsync(string server, long characterId, CancellationToken cancellationToken = default);
    Task<byte[]?> GetPortraitAsync(string server, long characterId, CancellationToken cancellationToken = default);
}
