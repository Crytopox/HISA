using Hisa.Core.Models.Intelligence;

namespace Hisa.Core.Abstractions;

public interface IIntelligenceHistoryRepository : IAsyncDisposable
{
    string DatabasePath { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<KillmailWriteResult> InsertBatchAsync(
        IReadOnlyList<KillmailObservation> observations,
        CancellationToken cancellationToken = default);

    Task RecordGapsAsync(
        IReadOnlyList<IntelligenceCollectionGap> gaps,
        CancellationToken cancellationToken = default);

    Task<int> DeleteOccurredBeforeAsync(
        DateTimeOffset cutoffUtc,
        int maxRows,
        CancellationToken cancellationToken = default);

    Task<int> DeleteGapsRecordedBeforeAsync(
        DateTimeOffset cutoffUtc,
        int maxRows,
        CancellationToken cancellationToken = default);

    Task<KillmailHistoryPage> GetTimeRangePageAsync(
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcInclusive,
        int limit,
        KillmailHistoryCursor? after,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntelligenceCollectionGap>> GetCollectionGapsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<long> RecordCoverageDeltaAsync(
        string checkpointName,
        string sessionId,
        long observedTotal,
        IntelligenceCollectionGap gap,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KillmailHistoryRecord>> GetByTimeRangeAsync(
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcInclusive,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KillmailHistoryRecord>> GetBySolarSystemAsync(
        long solarSystemId,
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcInclusive,
        int limit,
        CancellationToken cancellationToken = default);

    Task<KillmailHistoryDetail?> GetKillmailAsync(long killmailId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KillmailHistoryRecord>> GetRecentAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default);

    Task<IntelligenceHistoryHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}
