namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>The current alert state for one machine-and-metric pair.</summary>
public sealed class MetricAlertState
{
    /// <summary>The alert is open: the message has been sent and must not repeat.</summary>
    public bool IsAlerting { get; set; }

    /// <summary>How many checks in a row the value has stayed above the threshold.</summary>
    public int ConsecutiveHighSamples { get; set; }

    /// <summary>
    /// For availability events, the moment of the last reading before the machine went
    /// quiet. It is what lets the recovery message name the length of the outage. After a
    /// restart it is reconstructed from the journal: the Triggered event records how many
    /// minutes the machine had been silent when it fired, and subtracting that from its
    /// timestamp gives back the same instant.
    /// </summary>
    public DateTime? SilentSinceUtc { get; set; }
}
