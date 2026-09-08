namespace ServerMonitor.Domain.Entities;

public class AppSettings
{
    public int Id { get; set; }
    public double CpuThreshold { get; set; }
    public double MemoryThreshold { get; set; }
    public double DiskThreshold { get; set; }
    public bool AlertsEnabled { get; set; }

    /// <summary>
    /// How many seconds of silence count as a machine having gone missing. The fleet screen
    /// uses the same threshold, so there is one value for the whole system: were they to
    /// diverge, the interface and the notifications would contradict each other.
    /// </summary>
    public int OfflineAfterSeconds { get; set; } = 300;

    public bool HeartbeatAlertsEnabled { get; set; } = true;
}