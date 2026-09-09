namespace ServerMonitor.Api.Dtos;

/// <summary>A row of the server list: everything the fleet screen draws, with no follow-up calls.</summary>
public class ServerSummaryDto
{
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }
    public DateTime? LastSeenUtc { get; set; }

    /// <summary>Online, Stale or Offline as a string, so the frontend needs no reference to the domain.</summary>
    public string Health { get; set; } = string.Empty;

    /// <summary>Values from the latest reading. Empty when nothing has arrived from the machine yet.</summary>
    public double? CpuUsagePercent { get; set; }
    public double? MemoryUsagePercent { get; set; }
    public double? DiskUsagePercent { get; set; }

    /// <summary>Recent CPU values in ascending time order, for the sparkline in the row.</summary>
    public List<double> CpuTrend { get; set; } = new();
}

/// <summary>A new display name for a machine.</summary>
/// <remarks>
/// Only the name. The hostname, operating system and agent version are facts the agent reports
/// about itself, and letting the interface overwrite them would turn observations into opinions.
/// </remarks>
public class RenameServerRequest
{
    public string Name { get; set; } = string.Empty;
}
