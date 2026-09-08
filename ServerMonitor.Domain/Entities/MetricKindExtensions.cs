namespace ServerMonitor.Domain.Entities;

public static class MetricKindExtensions
{
    /// <summary>
    /// The human-readable name of a metric. The same string is what goes into the database,
    /// so rows written before the switch to enums still read back correctly.
    /// </summary>
    public static string ToDisplayName(this MetricKind kind) =>
        kind == MetricKind.Cpu ? "CPU" : kind.ToString();
}
