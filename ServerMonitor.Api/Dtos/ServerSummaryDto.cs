namespace ServerMonitor.Api.Dtos;

/// <summary>Строка списка серверов: всё, что нужно нарисовать в парке, без похода за деталями.</summary>
public class ServerSummaryDto
{
    public Guid PublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }
    public DateTime? LastSeenUtc { get; set; }

    /// <summary>Online, Stale или Offline — строкой, чтобы фронтенду не требовалась ссылка на домен.</summary>
    public string Health { get; set; } = string.Empty;

    /// <summary>Значения последнего замера. Пусто, если от машины ещё ничего не приходило.</summary>
    public double? CpuUsagePercent { get; set; }
    public double? MemoryUsagePercent { get; set; }
    public double? DiskUsagePercent { get; set; }

    /// <summary>Последние значения CPU по возрастанию времени — на спарклайн в строке.</summary>
    public List<double> CpuTrend { get; set; } = new();
}
