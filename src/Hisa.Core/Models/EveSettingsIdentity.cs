namespace Hisa.Core.Models;

public sealed record EveCharacterFile(string Path, string Server, string Profile, long CharacterId);
public sealed record EveCharacterIdentity(string Server, long CharacterId, string? Name, string Status, bool FromCache = false)
{
    public string DisplayName => Name ?? $"Character {CharacterId}";
}
public sealed record EveAccountGroup(string Key, string Server, long AccountId, IReadOnlyList<EveSettingsAccount> Files);
public sealed record EveLocalCharacterLink(string GroupKey, string Server, long CharacterId);

public static class EveSettingsIdentity
{
    public static string Server(string installation)
    {
        foreach (var server in new[] { "tranquility", "singularity", "serenity", "thunderdome" })
            if (installation.Contains(server, StringComparison.OrdinalIgnoreCase)) return server;
        return "unknown";
    }
    public static string GroupKey(EveSettingsAccount account)
    {
        var server = Server(account.Installation);
        var scope = server == "unknown" ? System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(account.Path))!).TrimEnd(System.IO.Path.DirectorySeparatorChar) : server;
        if (server=="unknown"&&OperatingSystem.IsWindows()) scope = scope.ToUpperInvariant();
        return $"{scope}|{account.AccountId}";
    }
}
