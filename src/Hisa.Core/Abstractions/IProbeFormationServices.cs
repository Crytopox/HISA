using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IProbeFormationLibrary
{
    Task<FormationLibrary> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(FormationLibrary library, CancellationToken cancellationToken = default);
    FormationLibrary Import(string json, FormationLibrary current, FormationImportAction conflicts);
    string Export(IEnumerable<ProbeFormation> formations);
}

public interface IEveProbeSettingsService
{
    Task<IReadOnlyList<EveSettingsAccount>> DiscoverAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default);
    Task<EveFormationSnapshot> ReadAsync(EveSettingsAccount account, CancellationToken cancellationToken = default);
    Task<string> GetFingerprintAsync(EveSettingsAccount account, CancellationToken cancellationToken = default);
    FormationMergePreview Preview(EveFormationSnapshot snapshot, IReadOnlyList<ProbeFormation> formations, IReadOnlyList<FormationMergeChoice> choices);
    EveClientStatus GetClientStatus();
    Task<EveSettingsApplyResult> ApplyAsync(FormationMergePreview preview, bool clientClosedAcknowledged, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EveSettingsBackup>> ListBackupsAsync(EveSettingsAccount account, CancellationToken cancellationToken = default);
    Task<EveSettingsApplyResult> RestoreAsync(EveSettingsAccount account, EveSettingsBackup backup, string expectedFingerprint, bool clientClosedAcknowledged, CancellationToken cancellationToken = default);
}
