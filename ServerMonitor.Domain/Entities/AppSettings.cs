namespace ServerMonitor.Domain.Entities;

public class AppSettings
{
    public int Id { get; set; }
    public double CpuThreshold { get; set; }
    public double MemoryThreshold { get; set; }
    public double DiskThreshold { get; set; }
    public bool AlertsEnabled { get; set; }

    /// <summary>
    /// Сколько секунд молчания считать пропажей машины. Тот же порог использует экран парка,
    /// поэтому он один на систему: расходись они, интерфейс и оповещения противоречили бы
    /// друг другу.
    /// </summary>
    public int OfflineAfterSeconds { get; set; } = 300;

    public bool HeartbeatAlertsEnabled { get; set; } = true;
}