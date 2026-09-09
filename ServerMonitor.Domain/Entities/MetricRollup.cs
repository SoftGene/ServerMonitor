namespace ServerMonitor.Domain.Entities;

/// <summary>
/// One hour of readings from one machine, reduced to a handful of numbers.
/// </summary>
/// <remarks>
/// Exists so that retention stops being pure loss. A reading every five seconds is 720 rows an
/// hour; this is one, and it survives after those are deleted. The trade is deliberate: a year
/// ago you can see that memory sat at 80% all week, and you cannot see the individual spike at
/// 14:32 on a Tuesday. Detail is what gets old; shape is what stays useful.
/// <para>
/// Percentages rather than the absolute megabytes the raw rows store, because these are meant to
/// be compared across months and the denominator can change — a RAM upgrade would otherwise make
/// the old numbers quietly incomparable to the new ones.
/// </para>
/// </remarks>
public class MetricRollup
{
    public int Id { get; set; }

    public int ServerId { get; set; }

    /// <summary>
    /// The hour this covers, truncated to the top of the hour, in UTC.
    /// </summary>
    /// <remarks>
    /// Unique together with <see cref="ServerId"/>. The uniqueness is not decoration: it is what
    /// lets the aggregation be re-run over a period it has already covered without producing a
    /// second copy of it.
    /// </remarks>
    public DateTime HourUtc { get; set; }

    /// <summary>
    /// How many readings went into this row.
    /// </summary>
    /// <remarks>
    /// Kept for two reasons. It says how much to trust the averages — an hour built from four
    /// readings is not the same claim as one built from seven hundred. And it is what makes a
    /// re-run safe: an aggregate computed from fewer samples than the stored one is never allowed
    /// to overwrite it, which matters when the raw rows behind an hour have already been deleted.
    /// </remarks>
    public int SampleCount { get; set; }

    public double CpuAvgPercent { get; set; }
    public double CpuMaxPercent { get; set; }

    public double MemoryAvgPercent { get; set; }
    public double MemoryMaxPercent { get; set; }

    public double DiskAvgPercent { get; set; }
    public double DiskMaxPercent { get; set; }
}
