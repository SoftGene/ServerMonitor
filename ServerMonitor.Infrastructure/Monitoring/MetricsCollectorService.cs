using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Infrastructure.Monitoring;

public class MetricsCollectorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMetricsCollector _collector;
    private readonly ILogger<MetricsCollectorService> _logger;

    private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);

    public MetricsCollectorService(
        IServiceScopeFactory scopeFactory,
        IMetricsCollector collector,
        ILogger<MetricsCollectorService> logger)
    {
        _scopeFactory = scopeFactory;
        _collector = collector;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Metrics collector service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _collector.CollectAsync(stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                dbContext.MetricSnapshots.Add(snapshot);
                await dbContext.SaveChangesAsync(stoppingToken);

                _logger.LogInformation(
                    "Snapshot saved: CPU={Cpu}%, RAM={RamUsed:F0}/{RamTotal:F0} MB",
                    snapshot.CpuUsagePercent,
                    snapshot.MemoryUsedMb,
                    snapshot.MemoryTotalMb);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while collecting metrics.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Metrics collector service stopped.");
    }
}