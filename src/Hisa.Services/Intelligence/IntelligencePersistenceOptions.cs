namespace Hisa.Services.Intelligence;

public sealed class IntelligencePersistenceOptions
{
    public const int DefaultQueueCapacity = 1024;
    public const int DefaultBatchSize = 64;
    public const int DefaultRetentionBatchSize = 500;
    public const int DefaultMaxRetentionBatchesPerCycle = 20;
    public const int DefaultMaxWriteAttempts = 5;
    public const int DefaultMaxQueryLimit = 1000;

    public string? DatabasePath { get; set; }

    public int QueueCapacity { get; set; } = DefaultQueueCapacity;

    public int BatchSize { get; set; } = DefaultBatchSize;

    public TimeSpan MaxBatchWait { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public int MaxWriteAttempts { get; set; } = DefaultMaxWriteAttempts;

    public TimeSpan WriteRetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(40);

    public TimeSpan WriteRetryMaxDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(48);
    public TimeSpan AggregateRetentionPeriod { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan CollectionHeartbeatMaxGap { get; set; } = TimeSpan.FromSeconds(90);

    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromMinutes(15);

    public int RetentionBatchSize { get; set; } = DefaultRetentionBatchSize;

    public int MaxRetentionBatchesPerCycle { get; set; } = DefaultMaxRetentionBatchesPerCycle;

    public TimeSpan ShutdownFlushTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan ShutdownGapReportTimeout { get; set; } = TimeSpan.FromSeconds(1);

    public int MaxQueryLimit { get; set; } = DefaultMaxQueryLimit;

    public int MaxSpoolEntries { get; set; } = 2048;

    public string? RecoverySpoolPath { get; set; }

    public TimeSpan SpoolRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan CoverageCheckpointInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan GapRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    public static string ResolveDatabasePath(IntelligencePersistenceOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            return Path.GetFullPath(options.DatabasePath);
        }

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HISA");
        return Path.Combine(directory, "hisa-intelligence.db");
    }

    public static string ResolveSpoolDirectory(IntelligencePersistenceOptions options, string databasePath)
    {
        if (!string.IsNullOrWhiteSpace(options.RecoverySpoolPath))
        {
            return Path.GetFullPath(options.RecoverySpoolPath);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (string.IsNullOrEmpty(directory))
        {
            directory = Path.GetTempPath();
        }

        return Path.Combine(directory, "hisa-intelligence-spool");
    }
}
