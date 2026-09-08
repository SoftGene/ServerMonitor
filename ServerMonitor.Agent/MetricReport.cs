namespace ServerMonitor.Agent;

/// <summary>
/// A reading in the shape it travels to the server in. It matches MetricReportDto in the API:
/// the agent and the server share no code, only the shape of the JSON.
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
