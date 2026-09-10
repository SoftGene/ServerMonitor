using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>The current alert state for one machine-and-metric pair.</summary>
public sealed class MetricAlertState
{
    /// <summary>
    /// For availability: an alert is open and must not repeat.
    /// </summary>
    /// <remarks>
    /// Availability has two states, so a flag is enough. CPU, memory and disk have three —
    /// normal, warning, critical — and keep theirs in <see cref="Threshold"/> instead.
    /// </remarks>
    public bool IsAlerting { get; set; }

    /// <summary>
    /// For CPU, memory and disk: the level in force and how many readings in a row have been
    /// above each threshold. Owned by <see cref="ThresholdRule"/>; this class only holds it.
    /// </summary>
    public ThresholdState Threshold { get; set; }

    /// <summary>
    /// For availability events, the moment of the last reading before the machine went
    /// quiet. It is what lets the recovery message name the length of the outage. After a
    /// restart it is reconstructed from the journal: the Triggered event records how many
    /// minutes the machine had been silent when it fired, and subtracting that from its
    /// timestamp gives back the same instant.
    /// </summary>
    public DateTime? SilentSinceUtc { get; set; }
}
