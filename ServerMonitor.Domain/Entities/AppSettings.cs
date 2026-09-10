namespace ServerMonitor.Domain.Entities;

public class AppSettings
{
    public int Id { get; set; }
    /// <summary>The critical threshold for CPU, in percent.</summary>
    /// <remarks>
    /// These three are the critical thresholds. They were simply "the threshold" before warning
    /// levels existed, and they keep their names: renaming a stored column and a public settings
    /// contract to make the code read slightly better would trade a real migration risk for a
    /// cosmetic gain. The documentation carries the meaning instead.
    /// </remarks>
    public double CpuThreshold { get; set; }

    /// <summary>The critical threshold for memory, in percent.</summary>
    public double MemoryThreshold { get; set; }

    /// <summary>The critical threshold for disk, in percent.</summary>
    public double DiskThreshold { get; set; }

    /// <summary>Above this, CPU is worth watching. May not be above <see cref="CpuThreshold"/>.</summary>
    /// <remarks>
    /// Setting it equal to the critical threshold switches warnings off for this metric, and the
    /// rule then behaves exactly as the single threshold did.
    /// </remarks>
    public double CpuWarningThreshold { get; set; } = 75;

    public double MemoryWarningThreshold { get; set; } = 75;

    public double DiskWarningThreshold { get; set; } = 75;
    public bool AlertsEnabled { get; set; }

    /// <summary>
    /// How many seconds of silence count as a machine having gone missing. The fleet screen
    /// uses the same threshold, so there is one value for the whole system: were they to
    /// diverge, the interface and the notifications would contradict each other.
    /// </summary>
    public int OfflineAfterSeconds { get; set; } = 300;

    public bool HeartbeatAlertsEnabled { get; set; } = true;
}