using Hisa.Logs.LocalChatLogs;

namespace Hisa.Logs.Tests;

public sealed class LocalChatLogParserTests
{
    private readonly LocalChatLogParser _parser = new();

    [Fact]
    public void Parse_ExtractsListenerAndSystemChanges_FromSampleOne()
    {
        var text = BuildSample(
            "Praefectus Manufactorum XV",
            "2026.03.04 16:36:22",
            "GPLB-C", "D-P1EH", "J-RXYN", "RYC-19", "GK5Z-T", "38NZ-1", "DG-8VJ", "0-6VZ5", "GPLB-C");

        var parsed = _parser.Parse(text);

        Assert.Equal("Praefectus Manufactorum XV", parsed.Listener);
        Assert.Equal(new DateTime(2026, 03, 04, 16, 36, 22, DateTimeKind.Utc), parsed.SessionStartedUtc);
        Assert.Equal(9, parsed.SystemChanges.Count);
        Assert.Equal("GPLB-C", parsed.SystemChanges[0].SolarSystemName);
        Assert.Equal("GPLB-C", parsed.SystemChanges[^1].SolarSystemName);
    }

    [Fact]
    public void Parse_ExtractsListenerAndSystemChanges_FromSampleTwo()
    {
        var text = BuildSample(
            "Veikath Haakario",
            "2026.03.17 22:12:18",
            "C-J6MT", "W-MF6J", "6-L4YC", "5E-CMA", "H6-EYX", "GPLB-C", "C-J6MT");

        var parsed = _parser.Parse(text);

        Assert.Equal("Veikath Haakario", parsed.Listener);
        Assert.Equal(new DateTime(2026, 03, 17, 22, 12, 18, DateTimeKind.Utc), parsed.SessionStartedUtc);
        Assert.Equal(7, parsed.SystemChanges.Count);
        Assert.Equal("C-J6MT", parsed.SystemChanges[0].SolarSystemName);
        Assert.Equal("C-J6MT", parsed.SystemChanges[^1].SolarSystemName);
    }

    private static string BuildSample(string listener, string sessionStarted, params string[] systems)
    {
        var lines = new List<string>
        {
            "\uFEFF------------------------------------------------------------",
            $"  Listener:        {listener}",
            $"  Session started: {sessionStarted}",
            "------------------------------------------------------------",
            "[ 2026.03.04 16:36:23 ] Random Pilot > ignored chat message"
        };

        lines.AddRange(systems.Select((system, index) =>
            $"[ 2026.03.04 16:{37 + index:00}:00 ] EVE System > Channel changed to Local : {system}"));

        return string.Join("\r\n", lines);
    }
}
