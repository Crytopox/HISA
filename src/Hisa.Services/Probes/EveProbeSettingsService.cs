using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.Services.Probes;

public sealed class EveProbeSettingsService : IEveProbeSettingsService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Func<EveClientStatus>? clientStatus;
    private readonly Action<string>? checkpoint;
    private readonly Func<IEnumerable<string>> defaultRoots = DefaultRoots;
    public EveProbeSettingsService() { }
    internal EveProbeSettingsService(Func<EveClientStatus> clientStatus, Action<string>? checkpoint = null, Func<IEnumerable<string>>? defaultRoots = null) { this.clientStatus = clientStatus; this.checkpoint = checkpoint; this.defaultRoots = defaultRoots ?? DefaultRoots; }

    public Task<IReadOnlyList<EveSettingsAccount>> DiscoverAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<EveSettingsAccount>>(() =>
    {
        var found = new Dictionary<string, EveSettingsAccount>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var queue = new Queue<(string Path, int Depth)>();
        foreach (var root in defaultRoots().Concat(roots).Distinct()) {
            if (File.Exists(root)) { Add(root); continue; }
            if (Directory.Exists(root)) queue.Enqueue((System.IO.Path.GetFullPath(root), 0));
        }
        var visited = new HashSet<string>(found.Comparer); var directories = 0;
        while (queue.TryDequeue(out var item)) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(item.Path) || item.Depth > 12 || ++directories > 30_000) continue;
            try {
                if ((File.GetAttributes(item.Path) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var file in Directory.EnumerateFiles(item.Path, "core_user_*.dat")) Add(file);
                foreach (var dir in Directory.EnumerateDirectories(item.Path))
                    if (!System.IO.Path.GetFileName(dir).Equals(".hisa-probe-backups", StringComparison.OrdinalIgnoreCase)) queue.Enqueue((dir, item.Depth + 1));
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return found.Values.OrderBy(a => a.Installation).ThenBy(a => a.Profile).ThenBy(a => a.AccountId).ToArray();
        void Add(string path) {
            var match = Regex.Match(System.IO.Path.GetFileName(path), @"^core_user_(\d+)\.dat$", RegexOptions.CultureInvariant);
            if (!match.Success || !long.TryParse(match.Groups[1].Value, out var id)) return;
            path = System.IO.Path.GetFullPath(path); var parent = Directory.GetParent(path)!;
            found[path] = new(path, parent.Parent?.Name ?? "Custom", parent.Name, id);
        }
    }, cancellationToken);

    private static IEnumerable<string> DefaultRoots()
    {
        if (OperatingSystem.IsWindows()) { yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP", "EVE"); yield break; }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS()) { yield return System.IO.Path.Combine(home, "Library/Application Support/CCP/EVE"); yield break; }
        foreach (var steam in new[] { ".local/share/Steam", ".steam/steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam" }) {
            var root = System.IO.Path.Combine(home, steam);
            yield return System.IO.Path.Combine(root, "steamapps/compatdata/8500/pfx/drive_c/users/steamuser/AppData/Local/CCP/EVE");
            var vdf = System.IO.Path.Combine(root, "steamapps/libraryfolders.vdf");
            string? text = null; try { if (File.Exists(vdf)) text = File.ReadAllText(vdf); } catch (IOException) { }
            if (text is not null) foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
                yield return System.IO.Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps/compatdata/8500/pfx/drive_c/users/steamuser/AppData/Local/CCP/EVE");
        }
        yield return System.IO.Path.Combine(home, ".wine/drive_c/users", Environment.UserName, "AppData/Local/CCP/EVE");
    }
    public async Task<EveFormationSnapshot> ReadAsync(EveSettingsAccount account, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(account.Path, cancellationToken);
        var doc = await Task.Run(() => BlueMarshalCodec.Decode(bytes), cancellationToken);
        var (formations, errors) = EveFormationData.Read(doc.Root);
        return new(account, Hash(bytes), formations, errors);
    }
    public async Task<string> GetFingerprintAsync(EveSettingsAccount account, CancellationToken cancellationToken = default) => Hash(await ReadBytesAsync(account.Path, cancellationToken));
    public FormationMergePreview Preview(EveFormationSnapshot snapshot, IReadOnlyList<ProbeFormation> formations, IReadOnlyList<FormationMergeChoice> choices)
    {
        if (snapshot.Errors.Count > 0) throw new InvalidDataException("Resolve malformed settings entries before writing: " + string.Join("; ", snapshot.Errors));
        if (formations.Select(f => f.Id).Distinct().Count() != formations.Count || choices.Select(c => c.LibraryId).Distinct().Count() != choices.Count)
            throw new ArgumentException("Duplicate formation choices.");
        var occupied = snapshot.Formations.Select(f => f.EveId).ToHashSet();
        var names = snapshot.Formations.ToDictionary(f => f.EveId, f => f.Formation.Name);
        var result = new List<FormationMergeEntry>(); var targets = new HashSet<long>();
        foreach (var f in formations) {
            ProbeFormationMath.Validate(f);
            var matches = snapshot.Formations.Where(e => string.Equals(e.Formation.Name, f.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            var choice = choices.SingleOrDefault(c => c.LibraryId == f.Id);
            if (matches.Length > 0 && (choice is null || choice.Action == FormationConflictAction.Unresolved)) throw new ArgumentException($"Choose how to handle '{f.Name}' in {snapshot.Account}.");
            if (choice?.Action == FormationConflictAction.KeepExisting) continue;
            long id; var entry = f;
            if (choice?.Action == FormationConflictAction.Replace) {
                if (choice.TargetId is not { } existing || !matches.Any(m => m.EveId == existing)) throw new ArgumentException("Select an existing matching formation ID to replace.");
                id = existing;
            } else {
                if (choice?.Action == FormationConflictAction.RenamedCopy) entry = f with { Name = choice.NewName?.Trim() ?? "" };
                ProbeFormationMath.Validate(entry);
                if (names.Values.Any(n => string.Equals(n, entry.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"A formation named '{entry.Name}' already exists; choose a different copy name.");
                id = 0; while (occupied.Contains(id)) id = checked(id + 1); occupied.Add(id);
            }
            if (!targets.Add(id)) throw new ArgumentException("Two changes target the same EVE formation.");
            names[id] = entry.Name;
            var existingFormation = snapshot.Formations.FirstOrDefault(s => s.EveId == id);
            if (existingFormation is not null && Equivalent(existingFormation.Formation, entry)) continue;
            result.Add(new(id, entry with { Probes = entry.Probes.ToArray() }, existingFormation is not null));
        }
        return new(snapshot, result.ToArray());
    }
    private static bool Equivalent(ProbeFormation a, ProbeFormation b) => a.Name == b.Name && a.Probes.SequenceEqual(b.Probes);
    public EveClientStatus GetClientStatus()
    {
        if (clientStatus is not null) return clientStatus();
        try {
            var inconclusive = !OperatingSystem.IsWindows();
            foreach (var process in Process.GetProcesses()) using (process) {
                try { var name = process.ProcessName; if (name.Equals("exefile", StringComparison.OrdinalIgnoreCase) || name.Equals("exefile.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("eve", StringComparison.OrdinalIgnoreCase)) return EveClientStatus.Running; }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { inconclusive = true; }
            }
            return inconclusive ? EveClientStatus.Unknown : EveClientStatus.Closed;
        } catch { return EveClientStatus.Unknown; }
    }
    public Task<EveSettingsApplyResult> ApplyAsync(FormationMergePreview preview, bool clientClosedAcknowledged, CancellationToken cancellationToken = default) =>
        WriteAsync(preview.Snapshot.Account, preview.Snapshot.Fingerprint, clientClosedAcknowledged, async original => {
            if (preview.Entries.Count == 0) return null;
            var source = BlueMarshalCodec.Decode(original); var (stored, errors) = EveFormationData.Read(source.Root);
            if (errors.Count > 0) throw new InvalidDataException(string.Join("; ", errors));
            foreach (var change in preview.Entries) {
                ProbeFormationMath.Validate(change.Formation);
                if (change.TargetId < 0 || change.ReplacesExisting != stored.Any(s => s.EveId == change.TargetId)) throw new InvalidDataException("Invalid merge target.");
            }
            if (preview.Entries.Select(e => e.TargetId).Distinct().Count() != preview.Entries.Count) throw new InvalidDataException("Duplicate merge targets.");
            var patched = EveFormationData.Patch(source.Root, preview.Entries);
            var output = BlueMarshalCodec.Encode(new(patched, source.HadChecksum));
            var verified = BlueMarshalCodec.Decode(output);
            if (!patched.Same(verified.Root) || !EveFormationData.WithoutFormations(source.Root).Same(EveFormationData.WithoutFormations(verified.Root))) throw new InvalidDataException("Unrelated settings preservation check failed.");
            var originalEntries = EveFormationData.Entries(source.Root);
            var newEntries = EveFormationData.Entries(verified.Root)!;
            if (originalEntries is not null) foreach (var p in originalEntries.Pairs) {
                if (p.Key.Kind == MarshalKind.Int && preview.Entries.Any(e => e.TargetId == p.Key.Integer)) continue;
                if (!newEntries.Pairs.Any(n => n.Key.Same(p.Key) && n.Value.Same(p.Value))) throw new InvalidDataException("An unselected formation changed.");
            }
            var readback = EveFormationData.Read(verified.Root);
            if (readback.Errors.Count > 0 || preview.Entries.Any(c => !readback.Formations.Any(f => f.EveId == c.TargetId && Equivalent(f.Formation, c.Formation)))) throw new InvalidDataException("Formation read-back verification failed.");
            return await Task.FromResult(output);
        }, cancellationToken);

    public async Task<IReadOnlyList<EveSettingsBackup>> ListBackupsAsync(EveSettingsAccount account, CancellationToken cancellationToken = default)
    {
        var dir = BackupDirectory(account.Path); if (!Directory.Exists(dir)) return [];
        var list = new List<EveSettingsBackup>();
        foreach (var metadata in Directory.EnumerateFiles(dir, "*.json")) {
            try {
                var backup = JsonSerializer.Deserialize<EveSettingsBackup>(await File.ReadAllTextAsync(metadata, cancellationToken));
                if (backup is not null && SamePath(backup.SourcePath, account.Path) && SamePath(System.IO.Path.GetDirectoryName(backup.Path)!, dir) && File.Exists(backup.Path)) list.Add(backup);
            } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return list.OrderByDescending(b => b.CreatedUtc).ToArray();
    }
    public Task<EveSettingsApplyResult> RestoreAsync(EveSettingsAccount account, EveSettingsBackup backup, string expectedFingerprint, bool clientClosedAcknowledged, CancellationToken cancellationToken = default) =>
        WriteAsync(account, expectedFingerprint, clientClosedAcknowledged, async _ => {
            if (!SamePath(backup.SourcePath, account.Path) || !SamePath(System.IO.Path.GetDirectoryName(backup.Path)!, BackupDirectory(account.Path))) throw new InvalidDataException("Backup belongs to another settings file.");
            EnsureNoLinks(backup.Path);
            var bytes = await ReadBytesAsync(backup.Path, cancellationToken);
            if (Hash(bytes) != backup.Sha256) throw new InvalidDataException("Backup checksum mismatch.");
            BlueMarshalCodec.Decode(bytes); return bytes;
        }, cancellationToken);

    private async Task<EveSettingsApplyResult> WriteAsync(EveSettingsAccount account, string expected, bool acknowledged, Func<byte[], Task<byte[]?>> build, CancellationToken ct)
    {
        var path = System.IO.Path.GetFullPath(account.Path); var gate = Gates.GetOrAdd(path, _ => new(1, 1));
        string? temp = null, backupPath = null; var entered = false;
        try {
            await gate.WaitAsync(ct); entered = true;
            CheckClient(acknowledged); ValidateTarget(path);
            var original = await ReadBytesAsync(path, ct);
            if (Hash(original) != expected) throw new IOException("Settings changed since preview. Refresh and preview again.");
            var output = await Task.Run(() => build(original), ct);
            if (output is null) return new(path, true, "No changes needed.");
            var dir = BackupDirectory(path); Directory.CreateDirectory(dir); EnsureNoLinks(dir);
            backupPath = System.IO.Path.Combine(dir, $"{System.IO.Path.GetFileName(path)}.{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}.{Guid.NewGuid():N}.bak");
            await WriteNewAsync(backupPath, original, ct);
            if (Hash(await ReadBytesAsync(backupPath, ct)) != expected) throw new IOException("Backup verification failed.");
            var backup = new EveSettingsBackup(backupPath, path, DateTimeOffset.UtcNow, expected);
            await WriteNewAsync(backupPath + ".json", JsonSerializer.SerializeToUtf8Bytes(backup), ct);
            checkpoint?.Invoke("backup");
            temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, $".hisa-probe-{Guid.NewGuid():N}.tmp");
            await WriteNewAsync(temp, output, ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
            var reread = await ReadBytesAsync(temp, ct); BlueMarshalCodec.Decode(reread);
            if (!reread.AsSpan().SequenceEqual(output)) throw new IOException("Temporary settings verification failed.");
            checkpoint?.Invoke("temporary"); CheckClient(acknowledged); ValidateTarget(path);
            if (Hash(await ReadBytesAsync(path, ct)) != expected) throw new IOException("Settings changed during write. Refresh and preview again.");
            ct.ThrowIfCancellationRequested(); checkpoint?.Invoke("replace");
            File.Replace(temp, path, null); temp = null;
            return new(path, true, "Settings updated and backup verified.", backupPath);
        } catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { return new(path, false, ex is OperationCanceledException ? "Operation canceled." : ex.Message, backupPath); }
        finally {
            if (temp is not null) try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (entered) gate.Release();
        }
    }
    private void CheckClient(bool acknowledged)
    {
        var status = GetClientStatus();
        if (status == EveClientStatus.Running) throw new IOException("Close all EVE clients before changing settings.");
        if (status == EveClientStatus.Unknown && !acknowledged) throw new IOException("Client detection is inconclusive. Confirm all EVE clients are closed.");
    }
    private static void ValidateTarget(string path)
    {
        if (!Regex.IsMatch(System.IO.Path.GetFileName(path), @"^core_user_\d+\.dat$")) throw new IOException("Select an existing account core_user file.");
        EnsureNoLinks(path);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new IOException("Settings file is missing or read-only.");
    }
    private static void EnsureNoLinks(string path)
    {
        for (var p = System.IO.Path.GetFullPath(path); p is not null; p = System.IO.Path.GetDirectoryName(p))
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("Choose a settings path without symbolic links or junctions.");
    }
    private static string BackupDirectory(string path) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!, ".hisa-probe-backups");
    private static bool SamePath(string a, string b) => string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static async Task<byte[]> ReadBytesAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length > BlueMarshalCodec.MaxBytes) throw new IOException("Settings file is too large.");
        var bytes = new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes, ct); return bytes;
    }
    private static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct); stream.Flush(true);
    }
}
