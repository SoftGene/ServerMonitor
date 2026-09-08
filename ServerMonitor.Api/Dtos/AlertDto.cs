namespace ServerMonitor.Api.Dtos;

public class AlertDto
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string MetricType { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Threshold { get; set; }
    public string AlertType { get; set; } = string.Empty;

    /// <summary>The machine the threshold fired on. In a shared list the event means nothing without it.</summary>
    public string ServerName { get; set; } = string.Empty;
    public Guid ServerPublicId { get; set; }
}
