namespace ServerMonitor.Web.Models;

/// <summary>
/// Строка списка серверов. Повторяет ServerSummaryDto из API: фронтенд намеренно
/// не ссылается на проекты бэкенда, связь между ними — только форма JSON.
/// </summary>
public class ServerSummary
{
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string Health { get; set; } = string.Empty;

    public double? CpuUsagePercent { get; set; }
    public double? MemoryUsagePercent { get; set; }
    public double? DiskUsagePercent { get; set; }

    public List<double> CpuTrend { get; set; } = new();

    public bool IsOffline => Health == "Offline";
    public bool HasReadings => CpuUsagePercent is not null;
}
