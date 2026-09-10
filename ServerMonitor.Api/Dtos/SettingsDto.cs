namespace ServerMonitor.Api.Dtos;

public class SettingsDto
{
    /// <summary>
    /// Critical thresholds, in percent. The names predate warning levels and were kept, since
    /// renaming them would break every client of this contract for no change in meaning.
    /// </summary>
    public double CpuThreshold { get; set; }
    public double MemoryThreshold { get; set; }
    public double DiskThreshold { get; set; }

    /// <summary>
    /// Warning thresholds, in percent. Equal to the critical threshold switches warnings off for
    /// that metric; above it is refused.
    /// </summary>
    public double CpuWarningThreshold { get; set; }
    public double MemoryWarningThreshold { get; set; }
    public double DiskWarningThreshold { get; set; }

    public bool AlertsEnabled { get; set; }

    /// <summary>How many seconds of silence count as a machine having gone missing.</summary>
    public int OfflineAfterSeconds { get; set; }

    public bool HeartbeatAlertsEnabled { get; set; }
}
