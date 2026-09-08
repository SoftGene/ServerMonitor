namespace ServerMonitor.Domain.Entities;

/// <summary>A watched machine with an agent installed on it.</summary>
public class Server
{
    public int Id { get; set; }

    /// <summary>
    /// The identifier used in URLs and the API. Kept separate from <see cref="Id"/> because
    /// sequential numbers in addresses reveal how many servers exist and invite enumeration.
    /// </summary>
    public Guid PublicId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }

    /// <summary>
    /// SHA-256 of the agent's personal key. Empty means no agent is bound to this row yet —
    /// a state only the row created by the migration for pre-existing history is ever in.
    /// </summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    public DateTime RegisteredAtUtc { get; set; }

    /// <summary>When data last arrived from the agent. Health is derived from it.</summary>
    public DateTime? LastSeenUtc { get; set; }
}
