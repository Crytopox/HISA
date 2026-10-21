using System.Text.RegularExpressions;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.Services.Probes;

public sealed class EveSettingsIdentityService(ISettingsService settings) : IEveSettingsIdentityService
{
    private readonly SemaphoreSlim linkLock = new(1,1);
    public IReadOnlyList<EveAccountGroup> GroupAccounts(IEnumerable<EveSettingsAccount> accounts) => accounts
        .GroupBy(EveSettingsIdentity.GroupKey, StringComparer.Ordinal)
        .Select(g => new EveAccountGroup(g.Key,EveSettingsIdentity.Server(g.First().Installation),g.First().AccountId,g.OrderBy(a => a.Profile).ThenBy(a => a.Path).ToArray()))
        .OrderBy(g => g.Server=="tranquility"?0:1).ThenBy(g=>g.Server).ThenBy(g => g.AccountId).ToArray();
    public Task<IReadOnlyList<EveCharacterFile>> DiscoverCharactersAsync(IEnumerable<EveSettingsAccount> accounts, CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<EveCharacterFile>>(() => {
        var result = new Dictionary<string,EveCharacterFile>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var account in accounts.DistinctBy(a => System.IO.Path.GetDirectoryName(a.Path))) {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = System.IO.Path.GetDirectoryName(account.Path)!;
            try {
                foreach (var path in Directory.EnumerateFiles(directory,"core_char_*.dat")) {
                    cancellationToken.ThrowIfCancellationRequested();
                    var match = Regex.Match(System.IO.Path.GetFileName(path),@"^core_char_(\d+)\.dat$");
                    if (match.Success && long.TryParse(match.Groups[1].Value,out var id) && id > 0)
                        result[path] = new(System.IO.Path.GetFullPath(path),EveSettingsIdentity.Server(account.Installation),account.Profile,id);
                }
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result.Values.OrderBy(c => c.Server).ThenBy(c => c.CharacterId).ToArray();
    },cancellationToken);
    public async Task<IReadOnlyList<EveLocalCharacterLink>> LoadLinksAsync(CancellationToken cancellationToken = default) =>
        await settings.GetAsync<EveLocalCharacterLink[]>("probes.identity-links.v1",cancellationToken) ?? [];
    public async Task SaveLinksAsync(string groupKey, string server, IEnumerable<long> characterIds, CancellationToken cancellationToken = default)
    {
        var ids = characterIds.Distinct().ToArray();
        if (string.IsNullOrWhiteSpace(groupKey) || ids.Any(id => id <= 0)) throw new ArgumentException("Choose valid character IDs.");
        await linkLock.WaitAsync(cancellationToken);
        try {
            var all = (await LoadLinksAsync(cancellationToken)).Where(l => l.GroupKey != groupKey).ToList();
            all.AddRange(ids.Select(id => new EveLocalCharacterLink(groupKey,server,id)));
            await settings.SetAsync("probes.identity-links.v1",all.ToArray(),cancellationToken);
        } finally { linkLock.Release(); }
    }
}
