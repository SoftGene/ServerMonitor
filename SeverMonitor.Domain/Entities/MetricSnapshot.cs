namespace ServerMonitor.Domain.Entities;

public class MetricSnapshot
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsedMb { get; set; }
    public double MemoryTotalMb { get; set; }
    public double DiskUsedGb { get; set; }
    public double DiskTotalGb { get; set; }
    public double UptimeSeconds { get; set; }

    /// <summary>
    /// Доля занятой памяти в процентах. В базе не хранится — вычисляется из абсолютных
    /// значений, поэтому формула живёт в одном месте.
    /// </summary>
    /// <remarks>
    /// Это обычное свойство C#, и в SQL оно не переводится: в запросах, где проекция
    /// выполняется на стороне базы, деление приходится писать выражением вручную.
    /// </remarks>
    public double MemoryUsagePercent =>
        MemoryTotalMb > 0 ? Math.Round(MemoryUsedMb / MemoryTotalMb * 100, 1) : 0;

    /// <summary>Доля занятого места на диске в процентах. В базе не хранится.</summary>
    public double DiskUsagePercent =>
        DiskTotalGb > 0 ? Math.Round(DiskUsedGb / DiskTotalGb * 100, 1) : 0;
}
