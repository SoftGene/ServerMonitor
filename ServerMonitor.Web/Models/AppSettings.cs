using System.Text.Json.Serialization;

namespace ServerMonitor.Web.Models;

public class AppSettings
{
    /// <summary>Critical thresholds. The names predate warning levels.</summary>
    public double CpuThreshold { get; set; }
    public double MemoryThreshold { get; set; }
    public double DiskThreshold { get; set; }

    /// <summary>Warning thresholds. Equal to critical switches warnings off for that metric.</summary>
    public double CpuWarningThreshold { get; set; }
    public double MemoryWarningThreshold { get; set; }
    public double DiskWarningThreshold { get; set; }

    public bool AlertsEnabled { get; set; }

    /// <summary>How many seconds of silence count as a machine having gone missing.</summary>
    public int OfflineAfterSeconds { get; set; }

    public bool HeartbeatAlertsEnabled { get; set; }

    /// <summary>
    /// The same threshold in minutes, for the form. Minutes suit a person and seconds suit
    /// storage, so the conversion lives in one place.
    /// </summary>
    [JsonIgnore]
    public int OfflineAfterMinutes
    {
        get => Math.Max(1, OfflineAfterSeconds / 60);
        set => OfflineAfterSeconds = Math.Max(1, value) * 60;
    }
}
