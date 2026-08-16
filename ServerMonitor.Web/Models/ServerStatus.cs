namespace ServerMonitor.Web.Models;
public class ServerStatus
{
    public DateTime TimeStampUtc { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public double MemoryUsedMb { get; set; }
    public double MemoryTotalMb { get; set; }
    public double DiskUsagePercent { get; set; }
    public double DiskUsedGb { get; set; }
    public double DiskTotalGb { get; set; }
    public string Uptime { get; set; } = string.Empty;
}
