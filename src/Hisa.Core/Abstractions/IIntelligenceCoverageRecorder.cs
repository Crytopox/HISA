using Hisa.Core.Models.Intelligence;

namespace Hisa.Core.Abstractions;

public interface IIntelligenceCoverageRecorder
{
    void Record(IntelligenceCollectionGap gap);
    void CollectionHeartbeat(DateTimeOffset now) { }
    void CollectionInterrupted(DateTimeOffset now) { }
}
