namespace ServerMonitor.Api.Dtos;

/// <summary>One hour of history, as kept after the raw readings behind it are gone.</summary>
/// <remarks>
/// Both the average and the peak are sent, and the pair is the point. An hour that averaged 40%
/// and an hour that averaged 40% while touching 99% are the same line on an averages-only chart,
/// and they are not the same hour. Averaging is what hides incidents, so the peak travels beside
/// it.
/// </remarks>
public class MetricTrendItemDto
{
    public DateTime HourUtc { get; set; }

    /// <summary>How many readings this hour was built from.</summary>
    /// <remarks>
    /// Sent so the interface can tell a quiet hour from a missing one. An hour built from four
    /// readings instead of seven hundred is a gap in the data, not a calm period.
    /// </remarks>
    public int SampleCount { get; set; }

    public double CpuAvgPercent { get; set; }
    public double CpuMaxPercent { get; set; }
    public double MemoryAvgPercent { get; set; }
    public double MemoryMaxPercent { get; set; }
    public double DiskAvgPercent { get; set; }
    public double DiskMaxPercent { get; set; }
}
