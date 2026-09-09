// The Web project is referenced under an alias: it and the API both have a Program class from
// their top-level statements, and without this the name would be ambiguous in this assembly.
extern alias WebApp;

using WebApp::ServerMonitor.Web.Presentation;

namespace ServerMonitor.Tests.Web;

/// <summary>
/// How a machine's state is worded on screen.
/// </summary>
/// <remarks>
/// These exist because of a real defect. The machine detail screen printed "Live" whenever the
/// browser was not paused — a fact about the page rather than the machine — so a host that had
/// stopped reporting hours earlier was announced as live, next to a clock that had stopped with
/// it. Nothing could have caught it: the wording was computed inline in the markup.
/// </remarks>
public class MachineStateTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Online", "Live")]
    [InlineData("Stale", "Stale")]
    [InlineData("Offline", "Offline")]
    public void EachHealth_HasItsOwnWord(string health, string expected)
    {
        Assert.Equal(expected, MachineState.Label(health));
    }

    [Theory]
    [InlineData("Offline")]
    [InlineData("Stale")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something-new-from-the-api")]
    public void AMachineThatIsNotHealthy_IsNeverCalledLive(string? health)
    {
        // The whole point. Anything that is not explicitly healthy reads as offline, including a
        // value this build does not recognise: claiming a machine is fine is the wrong way to be
        // wrong about it.
        Assert.NotEqual("Live", MachineState.Label(health));
        Assert.False(MachineState.IsReporting(health));
    }

    [Fact]
    public void OnlyAHealthyMachine_KeepsTheAnimationRunning()
    {
        // A dot that pulses green states that data is arriving. If it keeps pulsing after the
        // readings stop, the animation itself becomes the lie.
        Assert.True(MachineState.IsReporting("Online"));
        Assert.Equal(string.Empty, MachineState.DotClass("Online"));

        Assert.Equal("stale", MachineState.DotClass("Stale"));
        Assert.Equal("offline", MachineState.DotClass("Offline"));
        Assert.Equal("offline", MachineState.DotClass(null));
    }

    [Fact]
    public void AMachineNeverHeardFrom_SaysSo()
    {
        Assert.Equal("never", MachineState.LastSeen(null, Now));
    }

    [Theory]
    [InlineData(5, "5s ago")]
    [InlineData(90, "1m ago")]
    [InlineData(7200, "2h ago")]
    [InlineData(172800, "2d ago")]
    public void TheAgeIsRoundedDownToOneUnit(int secondsAgo, string expected)
    {
        Assert.Equal(expected, MachineState.LastSeen(Now.AddSeconds(-secondsAgo), Now));
    }

    [Fact]
    public void AClockRunningAhead_DoesNotProduceANegativeAge()
    {
        // The reading carries the agent's clock, and a machine a few seconds ahead of the server
        // would otherwise be reported as last seen "-3s ago".
        Assert.Equal("0s ago", MachineState.LastSeen(Now.AddSeconds(30), Now));
    }
}
