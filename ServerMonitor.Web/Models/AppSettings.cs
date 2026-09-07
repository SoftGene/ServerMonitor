using System.Text.Json.Serialization;

namespace ServerMonitor.Web.Models;

public class AppSettings
{
    public double CpuThreshold { get; set; }
    public double MemoryThreshold { get; set; }
    public double DiskThreshold { get; set; }
    public bool AlertsEnabled { get; set; }

    /// <summary>Сколько секунд молчания считать пропажей машины.</summary>
    public int OfflineAfterSeconds { get; set; }

    public bool HeartbeatAlertsEnabled { get; set; }

    /// <summary>
    /// Тот же порог в минутах — для формы. Человеку удобнее минуты, хранению нужны секунды,
    /// поэтому перевод живёт в одном месте.
    /// </summary>
    [JsonIgnore]
    public int OfflineAfterMinutes
    {
        get => Math.Max(1, OfflineAfterSeconds / 60);
        set => OfflineAfterSeconds = Math.Max(1, value) * 60;
    }
}