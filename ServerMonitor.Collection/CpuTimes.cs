namespace ServerMonitor.Collection;

/// <summary>
/// Accumulated CPU time counters at the moment of a reading.
/// Usage is calculated as the difference between two such readings.
/// </summary>
public readonly struct CpuTimes
{
    /// <summary>Idle time, in ticks or intervals depending on the source.</summary>
    public long Idle { get; init; }

    /// <summary>Total time across all states, idle included.</summary>
    public long Total { get; init; }
}
