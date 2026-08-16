namespace ServerMonitor.Web.Models;
public class MetricHistoryItem
{
    public DateTime TimestampUtc { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public double DiskUsagePercent { get; set; }
}
