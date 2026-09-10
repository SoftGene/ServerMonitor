using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Tests.Domain;

/// <summary>
/// Normal, warning and critical, and every way between them.
/// </summary>
/// <remarks>
/// Before this rule existed the same decision lived inside the alerting service with no tests at
/// all. These pin down the behaviour that already shipped — consecutive readings to raise,
/// immediate recovery, strictly-above — and then the new behaviour on top of it.
/// </remarks>
public class ThresholdRuleTests
{
    private const double Warning = 75;
    private const double Critical = 90;
    private const int Required = 3;

    /// <summary>Feeds readings in order and returns every event raised along the way.</summary>
    private static (ThresholdState State, List<ThresholdChange> Changes) Feed(
        ThresholdState start,
        params double[] values)
    {
        var state = start;
        var changes = new List<ThresholdChange>();

        foreach (var value in values)
        {
            var (next, change) = ThresholdRule.Evaluate(state, value, Warning, Critical, Required);

            state = next;

            if (change is not null)
            {
                changes.Add(change.Value);
            }
        }

        return (state, changes);
    }

    private static (ThresholdState State, List<ThresholdChange> Changes) Feed(params double[] values) =>
        Feed(default, values);

    private static ThresholdState Open(AlertSeverity level) => new(level, 0, 0);

    [Fact]
    public void BelowBothThresholds_NothingHappens()
    {
        var (state, changes) = Feed(10, 40, 70, 74);

        Assert.Empty(changes);
        Assert.Null(state.Level);
    }

    [Fact]
    public void ASingleSpike_RaisesNothing()
    {
        // Even a spike straight to 100%. One reading is noise; three in a row is a situation.
        var (state, changes) = Feed(100, 20, 20);

        Assert.Empty(changes);
        Assert.Null(state.Level);
    }

    [Fact]
    public void ADipResetsTheCount()
    {
        // Two above, one below, two above: never three in a row, so never an alert.
        var (_, changes) = Feed(95, 95, 50, 95, 95);

        Assert.Empty(changes);
    }

    [Fact]
    public void SustainedWarning_RaisesAWarning()
    {
        var (state, changes) = Feed(80, 80, 80);

        var change = Assert.Single(changes);

        Assert.Equal(AlertKind.Triggered, change.Kind);
        Assert.Equal(AlertSeverity.Warning, change.Severity);
        Assert.Equal(Warning, change.Threshold);
        Assert.Equal(AlertSeverity.Warning, state.Level);
    }

    [Fact]
    public void GoingStraightToCritical_RaisesOneCriticalAlertAndNoWarning()
    {
        // Without checking critical first this would send "warning" and then "critical" in the
        // same breath — two messages about one event, the first of them understating it.
        var (state, changes) = Feed(95, 95, 95);

        var change = Assert.Single(changes);

        Assert.Equal(AlertSeverity.Critical, change.Severity);
        Assert.Equal(Critical, change.Threshold);
        Assert.Equal(AlertSeverity.Critical, state.Level);
    }

    [Fact]
    public void AWarningThatWorsens_EscalatesToCritical()
    {
        var (state, changes) = Feed(80, 80, 80, 95, 95, 95);

        Assert.Equal(2, changes.Count);
        Assert.Equal(AlertSeverity.Warning, changes[0].Severity);
        Assert.Equal(AlertSeverity.Critical, changes[1].Severity);
        Assert.Equal(AlertSeverity.Critical, state.Level);
    }

    [Fact]
    public void EscalationAlsoNeedsConsecutiveReadings()
    {
        // Already in warning; one reading above critical is still just a spike.
        var (state, changes) = Feed(Open(AlertSeverity.Warning), 95, 80, 80);

        Assert.Empty(changes);
        Assert.Equal(AlertSeverity.Warning, state.Level);
    }

    [Fact]
    public void AnOpenAlert_DoesNotRepeat()
    {
        // The rule fires on transitions. Otherwise every half-minute check would announce
        // "still critical", which is the fastest way to teach people to ignore alerts.
        var (_, changes) = Feed(Open(AlertSeverity.Critical), 95, 97, 99, 95, 93);

        Assert.Empty(changes);
    }

    [Fact]
    public void CriticalEasingIntoWarning_Downgrades()
    {
        var (state, changes) = Feed(Open(AlertSeverity.Critical), 80);

        var change = Assert.Single(changes);

        // Reported as a warning now being open rather than as a recovery: after a restart the
        // state is rebuilt from the journal, and its last word must be the level still in force.
        Assert.Equal(AlertKind.Triggered, change.Kind);
        Assert.Equal(AlertSeverity.Warning, change.Severity);
        Assert.Equal(AlertSeverity.Warning, state.Level);
    }

    [Theory]
    [InlineData(AlertSeverity.Warning)]
    [InlineData(AlertSeverity.Critical)]
    public void DroppingBelowWarning_RecoversImmediately(AlertSeverity open)
    {
        // One reading is enough to recover, exactly as before levels existed: once the value has
        // eased, keeping the alert open longer is only the system being slow to say so.
        var (state, changes) = Feed(Open(open), 40);

        var change = Assert.Single(changes);

        Assert.Equal(AlertKind.Recovered, change.Kind);
        // The recovery names the level that closed — "critical is over" is different news from
        // "a warning is over".
        Assert.Equal(open, change.Severity);
        Assert.Null(state.Level);
    }

    [Fact]
    public void AValueExactlyOnTheWarningLine_HasNotCrossedIt()
    {
        // Strictly above, exactly as the single threshold was: sitting on the line is not over it.
        // Asserted on its own, because a test that only checks the final outcome cannot tell a
        // warning raised at 75 from one raised later at 90 — and that difference is the bug.
        var (state, changes) = Feed(Warning, Warning, Warning);

        Assert.Empty(changes);
        Assert.Null(state.Level);
    }

    [Fact]
    public void AValueExactlyOnTheCriticalLine_DoesNotEscalate()
    {
        var (state, changes) = Feed(Open(AlertSeverity.Warning), Critical, Critical, Critical);

        Assert.Empty(changes);
        Assert.Equal(AlertSeverity.Warning, state.Level);
    }

    [Fact]
    public void EqualThresholds_BehaveExactlyLikeTheOldSingleThreshold()
    {
        // Setting warning equal to critical switches warnings off. It has to degrade to precisely
        // what shipped before, or upgrading changes behaviour nobody asked to change.
        var state = default(ThresholdState);
        var changes = new List<ThresholdChange>();

        foreach (var value in new double[] { 95, 95, 95, 40 })
        {
            var (next, change) = ThresholdRule.Evaluate(state, value, 90, 90, Required);
            state = next;

            if (change is not null)
            {
                changes.Add(change.Value);
            }
        }

        Assert.Equal(2, changes.Count);
        Assert.Equal((AlertKind.Triggered, AlertSeverity.Critical), (changes[0].Kind, changes[0].Severity));
        Assert.Equal((AlertKind.Recovered, AlertSeverity.Critical), (changes[1].Kind, changes[1].Severity));
    }

    [Fact]
    public void RequiredSamplesBelowOne_IsTreatedAsOne()
    {
        // A misconfigured zero must not mean "never alert", and certainly not a division or a
        // negative count somewhere further down.
        var (next, change) = ThresholdRule.Evaluate(default, 95, Warning, Critical, requiredSamples: 0);

        Assert.NotNull(change);
        Assert.Equal(AlertSeverity.Critical, next.Level);
    }
}
