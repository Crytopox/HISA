using Microsoft.Extensions.Logging;
using Hisa.Services.Intelligence;

namespace Hisa.Services.Tests;

public class ZkillNormalizationDiagnosticsTests
{
    [Fact]
    public void RecordRejection_CountsAndThrottlesWithoutLoggingPayload()
    {
        var now = 1_000_000L;
        var logger = new ListLogger();
        var diagnostics = new ZkillNormalizationDiagnostics(logger, () => now);

        diagnostics.RecordRejection(10, KillmailNormalizationRejection.MissingTimestamp);
        diagnostics.RecordRejection(11, KillmailNormalizationRejection.MissingTimestamp);

        Assert.Equal(2, diagnostics.RejectionCount(KillmailNormalizationRejection.MissingTimestamp));
        var message = Assert.Single(logger.Messages);
        Assert.Contains("sequence 10", message, StringComparison.Ordinal);
        Assert.Contains(nameof(KillmailNormalizationRejection.MissingTimestamp), message, StringComparison.Ordinal);
        Assert.DoesNotContain("killmail_id", message, StringComparison.Ordinal);
        Assert.DoesNotContain("{", message, StringComparison.Ordinal);

        now += (long)ZkillNormalizationDiagnostics.LogInterval.TotalMilliseconds;
        diagnostics.RecordRejection(12, KillmailNormalizationRejection.MissingTimestamp);

        Assert.Equal(3, diagnostics.RejectionCount(KillmailNormalizationRejection.MissingTimestamp));
        Assert.Equal(2, logger.Messages.Count);
        Assert.Contains("sequence 12", logger.Messages[1], StringComparison.Ordinal);
        Assert.Contains("RejectionCount: 3", logger.Messages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void RecordRejection_DifferentReasons_AreCountedSeparately()
    {
        var logger = new ListLogger();
        var diagnostics = new ZkillNormalizationDiagnostics(logger, () => 0);

        diagnostics.RecordRejection(4, KillmailNormalizationRejection.MissingSolarSystem);
        diagnostics.RecordRejection(5, KillmailNormalizationRejection.MalformedJson);

        Assert.Equal(1, diagnostics.RejectionCount(KillmailNormalizationRejection.MissingSolarSystem));
        Assert.Equal(1, diagnostics.RejectionCount(KillmailNormalizationRejection.MalformedJson));
        Assert.Equal(0, diagnostics.RejectionCount(KillmailNormalizationRejection.MissingVictim));
        Assert.Equal(2, logger.Messages.Count);
    }

    [Fact]
    public void RecordDataGap_CountsSkippedSequencesWithoutPayload()
    {
        var logger = new ListLogger();
        var diagnostics = new ZkillNormalizationDiagnostics(logger, () => 0);

        diagnostics.RecordDataGap(44, 3);

        Assert.Equal(1, diagnostics.DataGapCount);
        var message = Assert.Single(logger.Messages);
        Assert.Contains("sequence 44", message, StringComparison.Ordinal);
        Assert.Contains("DataGapCount: 1", message, StringComparison.Ordinal);
        Assert.DoesNotContain("{", message, StringComparison.Ordinal);
        Assert.DoesNotContain("killmail_id", message, StringComparison.Ordinal);
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
