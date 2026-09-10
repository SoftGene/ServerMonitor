namespace ServerMonitor.Domain.Entities;

/// <summary>Where one metric of one machine currently stands against its two thresholds.</summary>
/// <param name="Level">The severity of the alert that is open, or null when none is.</param>
/// <param name="SamplesAboveWarning">Consecutive checks with the value above the warning threshold.</param>
/// <param name="SamplesAboveCritical">Consecutive checks with the value above the critical threshold.</param>
public readonly record struct ThresholdState(
    AlertSeverity? Level,
    int SamplesAboveWarning,
    int SamplesAboveCritical);

/// <summary>An event the rule has decided should be recorded and announced.</summary>
/// <param name="Kind">Whether a level was entered or everything returned to normal.</param>
/// <param name="Severity">
/// For <c>Triggered</c>, the level now open. For <c>Recovered</c>, the level that just closed —
/// "critical is over" and "a warning is over" are different news.
/// </param>
/// <param name="Threshold">The boundary that was crossed, so the message can name it.</param>
public readonly record struct ThresholdChange(AlertKind Kind, AlertSeverity Severity, double Threshold);

/// <summary>
/// Decides whether a metric has just moved between normal, warning and critical.
/// </summary>
/// <remarks>
/// A pure function, for the same reason <see cref="HeartbeatRule"/> is one. This logic used to
/// live inside the alerting service, where it had no tests at all — and adding a second
/// threshold to code nothing could reach would have been guessing twice.
/// <para>
/// The asymmetry is deliberate. Going <b>up</b> a level needs several consecutive readings, so a
/// single spike raises nothing. Going <b>down</b> is immediate: once the value has actually
/// eased, holding an alert open for another minute would only be the system being slow to
/// admit the problem is over.
/// </para>
/// </remarks>
public static class ThresholdRule
{
    /// <param name="state">Where the metric stood before this reading.</param>
    /// <param name="value">The reading, in percent.</param>
    /// <param name="warningThreshold">Above this, the metric is worth watching.</param>
    /// <param name="criticalThreshold">Above this, something needs doing.</param>
    /// <param name="requiredSamples">How many consecutive readings it takes to raise a level.</param>
    /// <returns>The new state, and the event to raise — or null when nothing changed.</returns>
    public static (ThresholdState Next, ThresholdChange? Change) Evaluate(
        ThresholdState state,
        double value,
        double warningThreshold,
        double criticalThreshold,
        int requiredSamples)
    {
        // Strictly above, exactly as the single threshold was: a value sitting on the line has not
        // crossed it. Any dip resets the count, which is what makes it "consecutive".
        var aboveWarning = value > warningThreshold ? state.SamplesAboveWarning + 1 : 0;
        var aboveCritical = value > criticalThreshold ? state.SamplesAboveCritical + 1 : 0;

        // Where this single reading lands, before any question of how long it has been there.
        AlertSeverity? observed = value > criticalThreshold ? AlertSeverity.Critical
            : value > warningThreshold ? AlertSeverity.Warning
            : null;

        var current = state.Level;

        if (Rank(observed) < Rank(current))
        {
            var eased = new ThresholdState(observed, aboveWarning, aboveCritical);

            // Easing from critical into warning is reported as a warning being open, not as a
            // recovery. The journal is what state is restored from after a restart, and its last
            // word for this metric has to be the level that is actually in force.
            var change = observed is null
                ? new ThresholdChange(AlertKind.Recovered, current!.Value, warningThreshold)
                : new ThresholdChange(AlertKind.Triggered, observed.Value, warningThreshold);

            return (eased, change);
        }

        var required = Math.Max(1, requiredSamples);

        // Critical is checked first, so a value that goes straight to critical and stays there
        // raises one critical alert rather than a warning followed by a critical a moment later.
        AlertSeverity? sustained = aboveCritical >= required ? AlertSeverity.Critical
            : aboveWarning >= required ? AlertSeverity.Warning
            : null;

        if (Rank(sustained) > Rank(current))
        {
            var raised = new ThresholdState(sustained, aboveWarning, aboveCritical);
            var threshold = sustained == AlertSeverity.Critical ? criticalThreshold : warningThreshold;

            return (raised, new ThresholdChange(AlertKind.Triggered, sustained!.Value, threshold));
        }

        // Nothing changed. The alert fires on the transition rather than on the state, otherwise
        // every check would announce "CPU is still critical".
        return (new ThresholdState(current, aboveWarning, aboveCritical), null);
    }

    private static int Rank(AlertSeverity? severity) => severity switch
    {
        AlertSeverity.Critical => 2,
        AlertSeverity.Warning => 1,
        _ => 0
    };
}
