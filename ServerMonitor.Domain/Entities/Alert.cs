namespace ServerMonitor.Domain.Entities;

/// <summary>The metric an event refers to.</summary>
public enum MetricKind
{
    Cpu,
    Memory,
    Disk,

    /// <summary>
    /// Machine availability. Strictly speaking not a metric: no agent sends this value, it is
    /// inferred from the absence of data. The event still lives in the shared journal because
    /// its shape is identical — a machine, a time, and a start and an end.
    /// Added last: values are stored as text, so the ordering of this enum carries no meaning.
    /// </summary>
    Availability
}

/// <summary>Direction of an event: a threshold was crossed, or the value came back to normal.</summary>
public enum AlertKind
{
    Triggered,
    Recovered
}

public class Alert
{
    public int Id { get; set; }

    /// <summary>The machine this event belongs to.</summary>
    public int ServerId { get; set; }
    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// This used to be a string, where a typo such as "Trigered" would not have stopped the
    /// compiler. Values are still stored as text in the database — a value converter in
    /// AppDbContext takes care of that — so older rows are read without migrating any data.
    /// </summary>
    public MetricKind MetricType { get; set; }

    public double Value { get; set; }
    public double Threshold { get; set; }
    public AlertKind AlertType { get; set; }
}
