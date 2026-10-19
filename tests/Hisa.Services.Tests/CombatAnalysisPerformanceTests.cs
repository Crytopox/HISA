using System.Diagnostics;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Services.Tests;

public sealed partial class IntelligenceActivityTests
{
    [Fact]
    public async Task Combat_Performance_ThreeThousandSystemsTwoDays_WithConcurrentIngestion()
    {
        await using var db = await Database.Create();
        const int systems = 3000, perSystemDay = 8, days = 2;
        const int total = systems * perSystemDay * days;
        var now = Epoch.AddHours(36);
        var wide = new ActivityRange(Epoch, Epoch.AddDays(2));
        for (var offset = 0; offset < total; offset += 128)
            await db.Repository.InsertBatchAsync(Enumerable.Range(offset, Math.Min(128, total - offset)).Select(index =>
            {
                var day = index / (systems * perSystemDay);
                var system = index / perSystemDay % systems + 1;
                return Observation(index + 1, system, Epoch.AddDays(day).AddMinutes(index % perSystemDay * 2), 123.4567m);
            }).ToArray());
        await db.Sql("PRAGMA wal_checkpoint(TRUNCATE);");
        var beforeAnalysis = new FileInfo(db.Path).Length;
        long id = total + 100;
        async Task<double[]> IngestionSamples()
        {
            var samples = new List<double>();
            for (var batch = 0; batch < 20; batch++)
            {
                var rows = Enumerable.Range(0, 64).Select(i => Observation(Interlocked.Increment(ref id), 5001, Epoch.AddDays(1).AddMinutes(i % 15), 1m)).ToArray();
                var timer = Stopwatch.StartNew(); await db.Repository.InsertBatchAsync(rows); samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            return samples.Order().ToArray();
        }
        var idleIngestion = await IngestionSamples();
        var analysisSamples = new List<double>();
        var analysisStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var analysisTotal = Stopwatch.StartNew();
        var allocatedBefore = GC.GetTotalAllocatedBytes();
        var process = Task.Run(async () =>
        {
            for (var iteration = 0; iteration < systems * days + 100; iteration++)
            {
                var timer = Stopwatch.StartNew();
                var processed = await db.Repository.ProcessCombatAsync(now, TimeSpan.FromDays(30), CombatOptions);
                analysisSamples.Add(timer.Elapsed.TotalMilliseconds); analysisStarted.TrySetResult();
                if (processed == 0) return;
            }
            Assert.Fail("Combat backlog did not drain within the bounded fixture.");
        });
        await analysisStarted.Task;
        var busyIngestion = await IngestionSamples();
        await process;
        var analysisMs = analysisTotal.Elapsed.TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
        analysisSamples.Sort();
        var mapTimes = new List<double>(); var pageTimes = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            var timer = Stopwatch.StartNew(); var map = await db.Repository.GetCombatMapAsync(wide);
            mapTimes.Add(timer.Elapsed.TotalMilliseconds); Assert.Equal(systems + 1, map.Markers.Count); Assert.False(map.Incomplete);
            timer.Restart(); var page = await db.Repository.GetCombatEventsAsync(new(wide));
            pageTimes.Add(timer.Elapsed.TotalMilliseconds); Assert.Equal(100, page.Events.Count); Assert.NotNull(page.Next);
        }
        mapTimes.Sort(); pageTimes.Sort();
        await db.Sql("PRAGMA wal_checkpoint(TRUNCATE);");
        var growth = new FileInfo(db.Path).Length - beforeAnalysis;
        var summaryBytes = await db.Scalar("SELECT SUM(length(CAST(Summary AS BLOB))) FROM CombatEvent;");
        var eventCount = await db.Scalar("SELECT COUNT(*) FROM CombatEvent;");
        Assert.Equal(systems * days + 1, eventCount);
        output.WriteLine($"Combat workload: {systems} systems / {days} UTC dates / {total} source killmails, plus 2560 concurrent/control observations. Events={eventCount}.");
        output.WriteLine($"Analysis drain={analysisMs:F1} ms; partition median={analysisSamples[analysisSamples.Count / 2]:F2} ms; p95={analysisSamples[(int)(analysisSamples.Count * .95)]:F2} ms; allocated across drain (includes concurrent ingestion)={allocated:N0} bytes.");
        output.WriteLine($"64-kill ingestion: idle median={idleIngestion[10]:F2} ms p95={idleIngestion[18]:F2} ms; analysis active median={busyIngestion[10]:F2} ms p95={busyIngestion[18]:F2} ms; incremental median delta={busyIngestion[10]-idleIngestion[10]:F2} ms.");
        output.WriteLine($"Combat all-system markers median={mapTimes[5]:F2} ms; 100-event page median={pageTimes[5]:F2} ms; database growth during analysis+1280 new records={growth:N0} bytes; summary JSON={summaryBytes:N0} bytes ({summaryBytes/eventCount} bytes/event).");
    }
}
