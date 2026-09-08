namespace ServerMonitor.Domain.Entities;

public class MetricSnapshot
{
    public int Id { get; set; }

    /// <summary>The machine this reading was taken from.</summary>
    public int ServerId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsedMb { get; set; }
    public double MemoryTotalMb { get; set; }
    public double DiskUsedGb { get; set; }
    public double DiskTotalGb { get; set; }
    public double UptimeSeconds { get; set; }

    /// <summary>
    /// Used memory as a percentage. Not stored — derived from the absolute values, so the
    /// formula lives in exactly one place.
    /// </summary>
    /// <remarks>
    /// This is an ordinary C# property and does not translate to SQL: in queries whose
    /// projection runs on the database side, the division has to be written out by hand.
    /// </remarks>
    public double MemoryUsagePercent =>
        MemoryTotalMb > 0 ? Math.Round(MemoryUsedMb / MemoryTotalMb * 100, 1) : 0;

    /// <summary>Used disk space as a percentage. Not stored either.</summary>
    public double DiskUsagePercent =>
        DiskTotalGb > 0 ? Math.Round(DiskUsedGb / DiskTotalGb * 100, 1) : 0;
}
