namespace ServerMonitor.Agent;

/// <summary>
/// Замер в том виде, в каком он уходит на сервер. Форма совпадает с MetricReportDto в API —
/// общего кода между агентом и сервером нет, связывает их только форма JSON.
/// </summary>
public class MetricReport
{
    public DateTime TimestampUtc { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsedMb { get; set; }
    public double MemoryTotalMb { get; set; }
    public double DiskUsedGb { get; set; }
    public double DiskTotalGb { get; set; }
    public double UptimeSeconds { get; set; }
}
