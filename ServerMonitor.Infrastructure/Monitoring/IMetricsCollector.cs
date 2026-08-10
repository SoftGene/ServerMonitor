using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Monitoring;

public interface IMetricsCollector
{
    Task<MetricSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}