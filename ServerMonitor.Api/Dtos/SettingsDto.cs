namespace ServerMonitor.Api.Dtos;

public class SettingsDto
{
    public double CpuThreshold { get; set; }
    public double MemoryThreshold { get; set; }
    public double DiskThreshold { get; set; }
    public bool AlertsEnabled { get; set; }

    /// <summary>How many seconds of silence count as a machine having gone missing.</summary>
    public int OfflineAfterSeconds { get; set; }

    public bool HeartbeatAlertsEnabled { get; set; }
}
