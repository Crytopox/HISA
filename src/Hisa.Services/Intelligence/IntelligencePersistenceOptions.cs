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

    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromMinutes(15);

    public int RetentionBatchSize { get; set; } = DefaultRetentionBatchSize;

    public int MaxRetentionBatchesPerCycle { get; set; } = DefaultMaxRetentionBatchesPerCycle;

    public TimeSpan ShutdownFlushTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public int MaxQueryLimit { get; set; } = DefaultMaxQueryLimit;

    public static string ResolveDatabasePath(IntelligencePersistenceOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            return Path.GetFullPath(options.DatabasePath);
        }

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HISA");
        return Path.Combine(directory, "hisa-intelligence.db");
    }
}
