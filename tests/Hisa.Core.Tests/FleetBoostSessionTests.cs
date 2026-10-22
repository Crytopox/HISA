using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public class FleetBoostSessionTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 23, 30, 2, DateTimeKind.Utc);
    [Fact]
    public void ReportsReplaceChargesPreserveMindlinkAndCountPilotsOnce()
    {
        var session = new FleetBoostSession(Now);
        Assert.True(session.Apply(new(Now, "Pilot", ["shield-extension", "shield-extension"], true)));
        Assert.False(session.Apply(new(Now, "PILOT", ["shield-extension", "shield-extension"], true)));
        session.Apply(new(Now.AddSeconds(1), "pilot", ["rapid-deployment"], null));
        var pilot = Assert.Single(session.Pilots);
        Assert.Equal("rapid-deployment", Assert.Single(pilot.ChargeIds));
        Assert.True(pilot.Mindlink);
    }
    [Fact]
    public void StandaloneMindlinkUpdatesExistingAndFutureReports()
    {
        var session = new FleetBoostSession(Now);
        session.Apply(new(Now, "Pilot", [], true));
        Assert.Empty(session.Pilots);
        session.Apply(new(Now.AddSeconds(1), "Pilot", ["shield-extension"], null));
        Assert.True(Assert.Single(session.Pilots).Mindlink);
        session.Apply(new(Now.AddSeconds(2), "Pilot", [], false));
        Assert.False(Assert.Single(session.Pilots).Mindlink);
        Assert.Single(Assert.Single(session.Pilots).ChargeIds);
    }
    [Fact]
    public void OldAndDuplicateMessagesCannotResurrectRemovedPilotsButNewReportsCan()
    {
        var session = new FleetBoostSession(Now);
        var report = new FleetBoostMessage(Now.AddSeconds(1), "Pilot", ["shield-extension"], null);
        session.Apply(report);
        Assert.True(session.Remove("PILOT"));
        Assert.False(session.Apply(report));
        Assert.False(session.Apply(report with { TimestampUtc = Now }));
        Assert.Empty(session.Pilots);
        Assert.True(session.Apply(report with { TimestampUtc = Now.AddSeconds(2) }));
        Assert.Single(session.Pilots);
    }
    [Fact]
    public void BoundarySecondIsAllowedButEarlierHistoryAndRememberedCopiesAreNot()
    {
        var session = new FleetBoostSession(Now.AddMilliseconds(500));
        var report = new FleetBoostMessage(Now, "Pilot", ["shield-extension"], null);
        Assert.False(session.Apply(report with { TimestampUtc = Now.AddSeconds(-1) }));
        session.RememberExisting(report);
        Assert.False(session.Apply(report));
        Assert.True(session.Apply(report with { Pilot = "New pilot" }));
    }
}
