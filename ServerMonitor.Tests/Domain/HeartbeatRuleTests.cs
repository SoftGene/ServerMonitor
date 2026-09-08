using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Tests.Domain;

public class HeartbeatRuleTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Offline = TimeSpan.FromMinutes(5);

    [Fact]
    public void NeverReported_IsNotAnAlert()
    {
        // The agent registered but never sent anything — an unfinished install rather than
        // an outage.
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: false, lastSeenUtc: null, Now, Offline));
    }

    [Fact]
    public void NeverReported_WhileAlerting_DoesNotRecover()
    {
        // A machine with no data never "recovers" on its own.
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: true, lastSeenUtc: null, Now, Offline));
    }

    [Fact]
    public void FreshData_WhileHealthy_ChangesNothing()
    {
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: false, Now.AddSeconds(-10), Now, Offline));
    }

    [Fact]
    public void SilenceBeyondThreshold_Triggers()
    {
        Assert.Equal(
            AlertKind.Triggered,
            HeartbeatRule.Evaluate(isAlerting: false, Now.AddMinutes(-6), Now, Offline));
    }

    [Fact]
    public void SilenceContinuing_DoesNotRepeat()
    {
        // The alert fires on the transition, not the state: otherwise "the machine is still
        // unreachable" would arrive every half minute.
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: true, Now.AddMinutes(-40), Now, Offline));
    }

    [Fact]
    public void DataReturns_Recovers()
    {
        Assert.Equal(
            AlertKind.Recovered,
            HeartbeatRule.Evaluate(isAlerting: true, Now.AddSeconds(-3), Now, Offline));
    }

    [Theory]
    [InlineData(299, null)]
    [InlineData(301, AlertKind.Triggered)]
    public void ThresholdBoundary(int secondsAgo, AlertKind? expected)
    {
        Assert.Equal(
            expected,
            HeartbeatRule.Evaluate(isAlerting: false, Now.AddSeconds(-secondsAgo), Now, Offline));
    }

    [Fact]
    public void ThresholdIsTakenFromTheArgument()
    {
        var lastSeen = Now.AddMinutes(-2);

        // The same instant, a different verdict under a different threshold. The threshold is
        // configurable, and the rule has to honour the value it was given.
        Assert.Null(HeartbeatRule.Evaluate(false, lastSeen, Now, TimeSpan.FromMinutes(5)));
        Assert.Equal(
            AlertKind.Triggered,
            HeartbeatRule.Evaluate(false, lastSeen, Now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ClockSkew_FutureTimestamp_IsTreatedAsFresh()
    {
        // An agent's clock can run ahead. A reading "from the future" is no reason to declare
        // a machine missing: the difference is negative and the threshold is not exceeded.
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: false, Now.AddMinutes(10), Now, Offline));
    }
}
