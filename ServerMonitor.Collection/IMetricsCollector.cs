using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Collection;

public interface IMetricsCollector
{
    Task<MetricSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}