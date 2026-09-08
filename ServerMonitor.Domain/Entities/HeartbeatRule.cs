namespace ServerMonitor.Domain.Entities;

/// <summary>
/// Decides whether a machine's availability has just changed.
/// </summary>
/// <remarks>
/// A pure function: both the current state and the current time arrive as arguments, nothing
/// is read from the database and nothing is sent anywhere. That is the only reason the rule
/// can be covered by tests at all — "now" is a constant in the test, so the result does not
/// depend on when the suite runs.
/// </remarks>
public static class HeartbeatRule
{
    /// <param name="isAlerting">An alert for this machine is already open.</param>
    /// <param name="lastSeenUtc">When data last arrived from the machine.</param>
    /// <param name="nowUtc">The moment of the check.</param>
    /// <param name="offlineAfter">How much silence counts as the machine being gone.</param>
    /// <returns>
    /// <c>Triggered</c> when the machine has just been judged unreachable, <c>Recovered</c>
    /// when it answers again, <c>null</c> when nothing changed and there is nothing to report.
    /// </returns>
    public static AlertKind? Evaluate(
        bool isAlerting,
        DateTime? lastSeenUtc,
        DateTime nowUtc,
        TimeSpan offlineAfter)
    {
        if (lastSeenUtc is null)
        {
            // No data has ever arrived: the agent registered but never reported. That is an
            // unfinished install rather than an outage — there is nothing to raise and
            // nothing to clear.
            return null;
        }

        // The difference can be negative when the agent's clock has run ahead. Such a reading
        // counts as fresh, and rightly so: clock skew is not evidence that a machine died.
        var isSilent = nowUtc - lastSeenUtc.Value > offlineAfter;

        if (isSilent && !isAlerting)
        {
            return AlertKind.Triggered;
        }

        if (!isSilent && isAlerting)
        {
            return AlertKind.Recovered;
        }

        // Nothing changed. The alert fires on the transition rather than on the state,
        // otherwise every check would announce "the machine is still unreachable".
        return null;
    }
}
