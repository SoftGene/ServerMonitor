namespace ServerMonitor.Web.Models;

/// <summary>
/// A row of the server list. Mirrors ServerSummaryDto from the API: the frontend deliberately
/// references no backend project, and the only thing binding them is the shape of the JSON.
/// </summary>
public class ServerSummary
{
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string Health { get; set; } = string.Empty;

    public double? CpuUsagePercent { get; set; }
    public double? MemoryUsagePercent { get; set; }
    public double? DiskUsagePercent { get; set; }

    public List<double> CpuTrend { get; set; } = new();

    /// <summary>This machine's own silence threshold in seconds, or null when it follows the fleet.</summary>
    public int? CustomOfflineAfterSeconds { get; set; }

    /// <summary>The fleet-wide threshold in seconds.</summary>
    public int FleetOfflineAfterSeconds { get; set; }

    public bool IsOffline => Health == "Offline";
    public bool HasReadings => CpuUsagePercent is not null;
}
