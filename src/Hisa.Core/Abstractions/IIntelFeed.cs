using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IIntelFeed
{
    event EventHandler<IntelChatReport>? ReportReceived;
    event EventHandler<IReadOnlyDictionary<long, IntelSystemSnapshot>>? SnapshotUpdated;
    IReadOnlyDictionary<long, IntelSystemSnapshot> Snapshot { get; }
    string? GetShipTypeName(int typeId);
    Task ApplySettingsAsync(CancellationToken cancellationToken = default);
}
