namespace ServerMonitor.Agent;

/// <summary>Agent settings, read from the "Agent" configuration section.</summary>
public class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Address of the central API, for example https://monitor.example.com.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// The shared enrollment token. Needed only on the first run: after registration the
    /// agent works from its personal key and the token is never used again.
    /// </summary>
    public string EnrollmentToken { get; set; } = string.Empty;

    public int CollectIntervalSeconds { get; set; } = 5;

    /// <summary>How many readings to hold while the server is unreachable. 720 is about an hour.</summary>
    public int BufferCapacity { get; set; } = 720;

    /// <summary>Ceiling for the retry delay, so a server that is down is not hammered.</summary>
    public int MaxRetryDelaySeconds { get; set; } = 300;

    public TimeSpan CollectInterval => TimeSpan.FromSeconds(Math.Max(1, CollectIntervalSeconds));

    public TimeSpan MaxRetryDelay => TimeSpan.FromSeconds(Math.Max(1, MaxRetryDelaySeconds));
}
