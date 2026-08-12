namespace ServerMonitor.Api.Dtos;

public class MetricHistoryItemDto
{
    public DateTime TimestampUtc { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public double DiskUsagePercent { get; set; }
}

