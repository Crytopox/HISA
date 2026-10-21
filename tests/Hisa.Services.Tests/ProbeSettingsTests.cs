using System.Numerics;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Services.Probes;

namespace Hisa.Services.Tests;

public sealed class ProbeSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hisa-probes-" + Guid.NewGuid().ToString("N"));
    private EveSettingsAccount Account => new(Path.Combine(directory, "settings_Default", "core_user_12345.dat"), "Tranquility", "Default", 12345);
    private byte[] Initial(bool crc = true)
    {
        var root = new MarshalValue(MarshalKind.Dict); var ui = new MarshalValue(MarshalKind.Dict); root.Set("ui", ui);
        root.Pairs.Insert(0, (new(MarshalKind.Bytes, new byte[] {255,128}), new(MarshalKind.Unicode, "Binary key")));
        ui.Set("unrelated", MarshalValue.Tuple(new(MarshalKind.Long, BigInteger.Parse("134280801871504062")), new(MarshalKind.Bytes, new byte[] { 255, 0, 128 })));
        ui.Set("probescanning.selectedFormationID", MarshalValue.Tuple(new(MarshalKind.Long, new BigInteger(1234)), MarshalValue.Int(-4)));
        var entries = new MarshalValue(MarshalKind.Dict); entries.Pairs.Add((MarshalValue.Int(-4), MarshalValue.Tuple(MarshalValue.Bytes("tempFormation"), new(MarshalKind.List))));
        ui.Set(EveFormationData.FormationKey, MarshalValue.Tuple(new(MarshalKind.Long, new BigInteger(1234)), entries));
        return BlueMarshalCodec.Encode(new(root, crc));
    }
    private async Task<byte[]> SetupAsync(bool crc = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Account.Path)!); var bytes = Initial(crc); await File.WriteAllBytesAsync(Account.Path, bytes); return bytes;
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task ApplyPreservesUnrelatedDataInternalEntriesSelectionAndChecksumAndRestores(bool crc)
    {
        var original = await SetupAsync(crc); var service = new EveProbeSettingsService(() => EveClientStatus.Closed);
        var snapshot = await service.ReadAsync(Account); Assert.Empty(snapshot.Formations); Assert.Empty(snapshot.Errors);
        var f = ProbeFormationMath.Create("precise 雪") with { Probes = [new(new(123456.789123, -500000, 0), .125 * ProbeFormationMath.AstronomicalUnit)] };
        var preview = service.Preview(snapshot, [f], []);
        var result = await service.ApplyAsync(preview, false); Assert.True(result.Success, result.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(result.BackupPath!));
        var modified = BlueMarshalCodec.Decode(await File.ReadAllBytesAsync(Account.Path));
        Assert.Equal(crc, modified.HadChecksum);
        Assert.True(EveFormationData.WithoutFormations(BlueMarshalCodec.Decode(original).Root).Same(EveFormationData.WithoutFormations(modified.Root)));
        Assert.Contains(EveFormationData.Entries(modified.Root)!.Pairs, p => p.Key.Integer == -4);
        var after = await service.ReadAsync(Account); Assert.Equal(f.Probes, Assert.Single(after.Formations).Formation.Probes);
        Assert.Equal(f.Name, after.Formations[0].Formation.Name);
        var noChanges = service.Preview(after, [f], [new(f.Id, FormationConflictAction.Replace, after.Formations[0].EveId)]);
        Assert.Empty(noChanges.Entries);
        Assert.True((await service.ApplyAsync(noChanges, false)).Success);
        var backup = Assert.Single(await service.ListBackupsAsync(Account));
        var restore = await service.RestoreAsync(Account, backup, after.Fingerprint, false); Assert.True(restore.Success, restore.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(Account.Path));
        Assert.Equal(2, (await service.ListBackupsAsync(Account)).Count);
    }
    [Fact]
    public async Task StaleSnapshotsAndRunningOrUnacknowledgedClientsCannotWrite()
    {
        var original = await SetupAsync(); var service = new EveProbeSettingsService(() => EveClientStatus.Closed);
        var preview = service.Preview(await service.ReadAsync(Account), [ProbeFormationMath.Create()], []);
        foreach (var status in new[] { EveClientStatus.Running, EveClientStatus.Unknown }) {
            var blocked = await new EveProbeSettingsService(() => status).ApplyAsync(preview, false);
            Assert.False(blocked.Success); Assert.Equal(original, await File.ReadAllBytesAsync(Account.Path));
        }
        await File.WriteAllBytesAsync(Account.Path, Initial(false));
        Assert.False((await service.ApplyAsync(preview, true)).Success);
        Assert.Equal(Initial(false), await File.ReadAllBytesAsync(Account.Path));
    }
    [Theory] [InlineData("backup")] [InlineData("temporary")] [InlineData("replace")]
    public async Task FailuresLeaveOriginalBytesAndNoTemporaryFiles(string stage)
    {
        var original = await SetupAsync(); var service = new EveProbeSettingsService(() => EveClientStatus.Closed, name => { if (name == stage) throw new IOException("Injected " + stage); });
        var preview = service.Preview(await service.ReadAsync(Account), [ProbeFormationMath.Create()], []);
        Assert.False((await service.ApplyAsync(preview, true)).Success);
        Assert.Equal(original, await File.ReadAllBytesAsync(Account.Path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Account.Path)!, "*.tmp"));
    }
    [Fact]
    public async Task ConcurrentEditAfterTemporaryWriteIsNotOverwritten()
    {
        await SetupAsync(); var changed = Initial(false);
        var service = new EveProbeSettingsService(() => EveClientStatus.Closed, stage => { if (stage == "temporary") File.WriteAllBytes(Account.Path, changed); });
        var preview = service.Preview(await service.ReadAsync(Account), [ProbeFormationMath.Create()], []);
        Assert.False((await service.ApplyAsync(preview, true)).Success); Assert.Equal(changed, await File.ReadAllBytesAsync(Account.Path));
    }
    [Fact]
    public async Task DiscoveryAcceptsRootProfileAndAccountFileWithoutDuplicatingTargets()
    {
        await SetupAsync(); await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(Account.Path)!, "core_char_54321.dat"), Initial());
        var found = await new EveProbeSettingsService(() => EveClientStatus.Closed, defaultRoots: () => []).DiscoverAsync([directory, Path.GetDirectoryName(Account.Path)!, Account.Path]);
        Assert.Single(found, a => a.Path == Account.Path); Assert.DoesNotContain(found, a => a.Path.Contains("core_char"));
    }
    [Fact]
    public async Task MergeRequiresExplicitConflictsAndRetainsIds()
    {
        await SetupAsync(); var service = new EveProbeSettingsService(() => EveClientStatus.Closed); var f = ProbeFormationMath.Create("test");
        var snapshot = await service.ReadAsync(Account);
        snapshot = snapshot with { Formations = [new(0, f), new(2, f with { Name = "other" })] };
        Assert.Throws<ArgumentException>(() => service.Preview(snapshot, [f], []));
        Assert.Empty(service.Preview(snapshot, [f], [new(f.Id, FormationConflictAction.KeepExisting)]).Entries);
        var changed = f with { Probes = [new(default, 5)] };
        Assert.Equal(0, Assert.Single(service.Preview(snapshot, [changed], [new(f.Id, FormationConflictAction.Replace, 0)]).Entries).TargetId);
        Assert.Equal(1, Assert.Single(service.Preview(snapshot, [f], [new(f.Id, FormationConflictAction.RenamedCopy, NewName: "copy")]).Entries).TargetId);
        Assert.Throws<ArgumentException>(() => service.Preview(snapshot, [f], [new(f.Id, FormationConflictAction.Replace, 2)]));
    }
    [Fact]
    public async Task MalformedEntriesBlockWritesInsteadOfBeingDropped()
    {
        await SetupAsync(); var doc = BlueMarshalCodec.Decode(Initial());
        EveFormationData.Entries(doc.Root)!.Pairs.Add((MarshalValue.Int(1), MarshalValue.Int(2)));
        await File.WriteAllBytesAsync(Account.Path, BlueMarshalCodec.Encode(doc));
        var service = new EveProbeSettingsService(); var snapshot = await service.ReadAsync(Account);
        Assert.NotEmpty(snapshot.Errors); Assert.Throws<InvalidDataException>(() => service.Preview(snapshot, [ProbeFormationMath.Create()], []));
    }
    [Fact]
    public async Task DamagedBackupCannotBeRestored()
    {
        await SetupAsync(); var service = new EveProbeSettingsService(() => EveClientStatus.Closed);
        await service.ApplyAsync(service.Preview(await service.ReadAsync(Account), [ProbeFormationMath.Create()], []), true);
        var snapshot = await service.ReadAsync(Account); var bytes = await File.ReadAllBytesAsync(Account.Path); var backup = Assert.Single(await service.ListBackupsAsync(Account));
        await File.WriteAllBytesAsync(backup.Path, [0]);
        Assert.False((await service.RestoreAsync(Account, backup, snapshot.Fingerprint, true)).Success); Assert.Equal(bytes, await File.ReadAllBytesAsync(Account.Path));
    }
    [Fact]
    public async Task RestoreCanRecoverACorruptedCurrentSettingsFile()
    {
        var original = await SetupAsync(); var service = new EveProbeSettingsService(() => EveClientStatus.Closed);
        Assert.True((await service.ApplyAsync(service.Preview(await service.ReadAsync(Account), [ProbeFormationMath.Create()], []), true)).Success);
        var backup = Assert.Single(await service.ListBackupsAsync(Account));
        await File.WriteAllBytesAsync(Account.Path, [0, 1, 2]);
        var result = await service.RestoreAsync(Account, backup, await service.GetFingerprintAsync(Account), true);
        Assert.True(result.Success, result.Message); Assert.Equal(original, await File.ReadAllBytesAsync(Account.Path));
        Assert.Equal(new byte[] {0,1,2}, await File.ReadAllBytesAsync(result.BackupPath!));
    }
    [Fact]
    public async Task BackupCreationAndReadOnlyFailuresDoNotModifyOriginal()
    {
        var original = await SetupAsync(); var service = new EveProbeSettingsService(() => EveClientStatus.Closed);
        var preview = service.Preview(await service.ReadAsync(Account), [ProbeFormationMath.Create()], []);
        var blockedDir = Path.Combine(Path.GetDirectoryName(Account.Path)!, ".hisa-probe-backups");
        await File.WriteAllTextAsync(blockedDir, "not a directory");
        Assert.False((await service.ApplyAsync(preview, true)).Success); Assert.Equal(original, await File.ReadAllBytesAsync(Account.Path));
        File.Delete(blockedDir); File.SetAttributes(Account.Path, FileAttributes.ReadOnly);
        try { Assert.False((await service.ApplyAsync(preview, true)).Success); Assert.Equal(original, await File.ReadAllBytesAsync(Account.Path)); }
        finally { File.SetAttributes(Account.Path, FileAttributes.Normal); }
    }
    [Fact]
    public async Task BatchAccountsAreIndependentAndCanceledWritesAreSafe()
    {
        var original = await SetupAsync(); var service = new EveProbeSettingsService(() => EveClientStatus.Closed);
        var second = Account with { Path = Path.Combine(Path.GetDirectoryName(Account.Path)!, "core_user_67890.dat"), AccountId = 67890 };
        await File.WriteAllBytesAsync(second.Path, original);
        var f = ProbeFormationMath.Create(); var firstPreview = service.Preview(await service.ReadAsync(Account), [f], []); var secondPreview = service.Preview(await service.ReadAsync(second), [f], []);
        await File.WriteAllBytesAsync(second.Path, Initial(false));
        Assert.True((await service.ApplyAsync(firstPreview, true)).Success); Assert.False((await service.ApplyAsync(secondPreview, true)).Success);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.False((await service.ApplyAsync(secondPreview, true, cancellation.Token)).Success);
        Assert.Equal(Initial(false), await File.ReadAllBytesAsync(second.Path));
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}

internal sealed class ProbeMemorySettings : ISettingsService
{
    private readonly Dictionary<string, string> values = [];
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(values.TryGetValue(key, out var value) ? System.Text.Json.JsonSerializer.Deserialize<T>(value) : default);
    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) { values[key] = System.Text.Json.JsonSerializer.Serialize(value); return Task.CompletedTask; }
}
