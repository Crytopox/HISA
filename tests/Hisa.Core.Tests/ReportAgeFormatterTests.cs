using Hisa.Core;

namespace Hisa.Core.Tests;

public sealed class ReportAgeFormatterTests
{
    [Theory]
    [InlineData(-10, "0s ago")]
    [InlineData(0, "0s ago")]
    [InlineData(59, "59s ago")]
    [InlineData(60, "1m 00s ago")]
    [InlineData(125, "2m 05s ago")]
    [InlineData(3599, "59m 59s ago")]
    [InlineData(3600, "1h 00m 00s ago")]
    [InlineData(3723, "1h 02m 03s ago")]
    [InlineData(90061, "1d 01h 01m 01s ago")]
    public void ReportAgesIncludeSecondsAndHandleBoundaries(double seconds, string expected) =>
        Assert.Equal(expected, ReportAgeFormatter.Format(TimeSpan.FromSeconds(seconds)));
}
