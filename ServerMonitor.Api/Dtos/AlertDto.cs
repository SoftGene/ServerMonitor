namespace ServerMonitor.Api.Dtos;

public class AlertDto
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string MetricType { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Threshold { get; set; }
    public string AlertType { get; set; } = string.Empty;
}