namespace ServerMonitor.Web.Models;

public class Alert
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string MetricType { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Threshold { get; set; }
    public string AlertType { get; set; } = string.Empty;

    /// <summary>"Warning" or "Critical"; for a recovery, the level that closed.</summary>
    /// <remarks>
    /// Critical when absent, which is the honest reading of an event from before levels existed:
    /// it fired at the only threshold there was.
    /// </remarks>
    public string Severity { get; set; } = "Critical";

    /// <summary>The machine the threshold fired on.</summary>
    public string ServerName { get; set; } = string.Empty;
    public Guid ServerPublicId { get; set; }
}
