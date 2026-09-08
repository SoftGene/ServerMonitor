namespace ServerMonitor.Infrastructure.Monitoring;

/// <summary>
/// Alerting settings, read from the "Monitoring" section of the API configuration.
/// <para>
/// The collection interval no longer lives here: collecting is the job of the agent on the
/// watched machine, and how often it samples is its own setting
/// (<c>Agent:CollectIntervalSeconds</c>). Keeping it in the server's configuration would be a
/// lie — the server has no influence over it.
/// </para>
/// </summary>
public class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    /// <summary>The pause between threshold checks.</summary>
    public int AlertCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// How many checks in a row a value has to exceed the threshold before a notification
    /// goes out. Protection against flapping: a single spike raises nothing.
    /// </summary>
    public int AlertConsecutiveSamples { get; set; } = 3;

    public TimeSpan AlertCheckInterval => TimeSpan.FromSeconds(Math.Max(1, AlertCheckIntervalSeconds));

    public int RequiredConsecutiveSamples => Math.Max(1, AlertConsecutiveSamples);
}
