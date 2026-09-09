namespace ServerMonitor.Web.Models;

/// <summary>One hour of history, as it survives after the raw readings are deleted.</summary>
public class MetricTrendItem
{
    public DateTime HourUtc { get; set; }
    public int SampleCount { get; set; }
    public double CpuAvgPercent { get; set; }
    public double CpuMaxPercent { get; set; }
    public double MemoryAvgPercent { get; set; }
    public double MemoryMaxPercent { get; set; }
    public double DiskAvgPercent { get; set; }
    public double DiskMaxPercent { get; set; }
}
