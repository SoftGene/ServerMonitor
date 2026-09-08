namespace ServerMonitor.Infrastructure.Retention;

/// <summary>
/// How long readings are kept, read from the "Retention" section of the API configuration.
/// </summary>
/// <remarks>
/// Deliberately not in the settings table beside the alert thresholds, and so deliberately not
/// editable from the web interface. Changing a threshold is reversible — the old value can be
/// typed back in. Deleting three months of history is not, and a control that destroys data
/// should not sit one click away from a control that changes a number.
/// <para>
/// It is also a different kind of decision: how much disk this instance is allowed to consume,
/// which belongs to whoever deploys it rather than to whoever watches the graphs. Prometheus
/// makes the same call — its retention is a command-line flag, not a dashboard setting.
/// </para>
/// </remarks>
public class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>
    /// How many days of readings to keep. Zero or less disables the sweep entirely.
    /// </summary>
    /// <remarks>
    /// Zero means "keep everything", never "delete everything". The same rule as the unset
    /// service key in the API: when a setting that governs a destructive action is missing or
    /// nonsensical, the safe reading is the one that does nothing.
    /// </remarks>
    public int SnapshotDays { get; set; } = 30;

    /// <summary>The pause between sweeps.</summary>
    public int SweepIntervalHours { get; set; } = 6;

    /// <summary>
    /// How many rows one DELETE statement removes before starting another.
    /// </summary>
    /// <remarks>
    /// The first sweep on a database that has been collecting for months can match millions of
    /// rows. Deleting them in one statement holds a transaction open for the whole operation,
    /// blocks nothing but writes a great deal of WAL at once, and cannot be interrupted halfway.
    /// Batches make the work resumable: whatever a shutdown interrupts, the next sweep finishes.
    /// </remarks>
    public int BatchSize { get; set; } = 10_000;

    public bool IsEnabled => SnapshotDays > 0;

    public TimeSpan SweepInterval => TimeSpan.FromHours(Math.Max(1, SweepIntervalHours));

    public TimeSpan SnapshotLifetime => TimeSpan.FromDays(Math.Max(1, SnapshotDays));

    public int EffectiveBatchSize => Math.Clamp(BatchSize, 100, 100_000);
}
