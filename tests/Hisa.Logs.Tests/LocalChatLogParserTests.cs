using Hisa.Logs.LocalChatLogs;

namespace Hisa.Logs.Tests;

public sealed class LocalChatLogParserTests
{
    private readonly LocalChatLogParser _parser = new();

    [Fact]
    public void Parse_ExtractsListenerAndSystemChanges_FromSampleOne()
    {
        var text = ReadSample("Local_20260304_163622_2123313092.txt");

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
        var text = ReadSample("Local_20260317_221218_97100207.txt");

        var parsed = _parser.Parse(text);

        Assert.Equal("Veikath Haakario", parsed.Listener);
        Assert.Equal(new DateTime(2026, 03, 17, 22, 12, 18, DateTimeKind.Utc), parsed.SessionStartedUtc);
        Assert.Equal(7, parsed.SystemChanges.Count);
        Assert.Equal("C-J6MT", parsed.SystemChanges[0].SolarSystemName);
        Assert.Equal("C-J6MT", parsed.SystemChanges[^1].SolarSystemName);
    }

    private static string ReadSample(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.ReadAllText(path);
    }
}
