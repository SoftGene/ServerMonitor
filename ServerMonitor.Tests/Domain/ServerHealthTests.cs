using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Tests.Domain;

public class ServerHealthTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NeverSeen_IsOffline()
    {
        // The server registered but has not sent a single reading.
        Assert.Equal(ServerHealth.Offline, ServerHealthCalculator.FromLastSeen(null, Now));
    }

    [Theory]
    [InlineData(0, ServerHealth.Online)]
    [InlineData(59, ServerHealth.Online)]
    [InlineData(61, ServerHealth.Stale)]
    [InlineData(299, ServerHealth.Stale)]
    [InlineData(301, ServerHealth.Offline)]
    public void HealthDependsOnDataAge(int secondsAgo, ServerHealth expected)
    {
        var lastSeen = Now.AddSeconds(-secondsAgo);

        Assert.Equal(expected, ServerHealthCalculator.FromLastSeen(lastSeen, Now));
    }

    [Fact]
    public void ExactBoundaries_StayInTheHealthierState()
    {
        // The comparison is strict (>), so exactly on the boundary the state does not change yet.
        Assert.Equal(
            ServerHealth.Online,
            ServerHealthCalculator.FromLastSeen(Now - ServerHealthCalculator.StaleAfter, Now));

        Assert.Equal(
            ServerHealth.Stale,
            ServerHealthCalculator.FromLastSeen(Now - ServerHealthCalculator.OfflineAfter, Now));
    }

    [Fact]
    public void ClockSkew_DoesNotBreakTheCalculation()
    {
        // A timestamp from the future — the agent's clock ran ahead — makes the age negative,
        // and the server counts as alive rather than down.
        Assert.Equal(ServerHealth.Online, ServerHealthCalculator.FromLastSeen(Now.AddMinutes(10), Now));
    }

    [Fact]
    public void ExplicitThresholds_OverrideTheDefaults()
    {
        var lastSeen = Now.AddSeconds(-90);

        // By default 90 seconds of silence is Stale.
        Assert.Equal(ServerHealth.Stale, ServerHealthCalculator.FromLastSeen(lastSeen, Now));

        // With stricter thresholds the same instant already means Offline.
        Assert.Equal(
            ServerHealth.Offline,
            ServerHealthCalculator.FromLastSeen(
                lastSeen,
                Now,
                staleAfter: TimeSpan.FromSeconds(15),
                offlineAfter: TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void OnlyOneThresholdCanBeOverridden()
    {
        var lastSeen = Now.AddSeconds(-30);

        // The Stale threshold is raised and Offline keeps its default, so 30 seconds is still Online.
        Assert.Equal(
            ServerHealth.Online,
            ServerHealthCalculator.FromLastSeen(lastSeen, Now, staleAfter: TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void AMachineWithItsOwnThreshold_UsesIt()
    {
        Assert.Equal(TimeSpan.FromHours(12), ServerHealthCalculator.ResolveOfflineAfter(43200, 300));
    }

    [Fact]
    public void AMachineWithoutOne_FollowsTheFleet()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), ServerHealthCalculator.ResolveOfflineAfter(null, 600));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(-5, -1)]
    public void NothingUsable_FallsBackToTheBuiltInDefault(int? machine, int? fleet)
    {
        // Zero or negative must never become "offline immediately": every machine would be
        // reported missing between two perfectly normal readings.
        Assert.Equal(
            ServerHealthCalculator.OfflineAfter,
            ServerHealthCalculator.ResolveOfflineAfter(machine, fleet));
    }

    [Fact]
    public void AnUnusableMachineValue_DoesNotHideAUsableFleetOne()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), ServerHealthCalculator.ResolveOfflineAfter(0, 600));
    }

    [Fact]
    public void TheSameSilence_ReadsDifferentlyUnderDifferentThresholds()
    {
        // The feature in one assertion: two hours of silence is an outage for a server and an
        // ordinary evening for a laptop that is allowed twelve.
        var lastSeen = Now.AddHours(-2);

        var server = ServerHealthCalculator.FromLastSeen(lastSeen, Now,
            offlineAfter: ServerHealthCalculator.ResolveOfflineAfter(null, 300));
        var laptop = ServerHealthCalculator.FromLastSeen(lastSeen, Now,
            offlineAfter: ServerHealthCalculator.ResolveOfflineAfter(43200, 300));

        Assert.Equal(ServerHealth.Offline, server);
        Assert.NotEqual(ServerHealth.Offline, laptop);
    }
}
