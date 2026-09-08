using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ServerMonitor.Infrastructure.Retention;

/// <summary>
/// Runs the sweep on a schedule. All it owns is the timing.
/// </summary>
public class RetentionService : BackgroundService
{
    // Long enough for migrations and the first agents to settle. Deleting old rows is the least
    // urgent thing this process does, and competing with startup for the database is pointless
    // when the work can wait a minute.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetentionOptions _options;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<RetentionOptions> options,
        ILogger<RetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEnabled)
        {
            // Said out loud on purpose. A system that quietly keeps everything forever looks
            // exactly like a system with a retention policy, right up until the disk fills.
            _logger.LogWarning(
                "Retention is disabled (Retention:SnapshotDays is {Days}); readings are kept forever.",
                _options.SnapshotDays);

            return;
        }

        _logger.LogInformation(
            "Retention service started; keeping {Days} days of readings, sweeping every {Hours} h.",
            _options.SnapshotDays,
            _options.SweepInterval.TotalHours);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var sweeper = scope.ServiceProvider.GetRequiredService<SnapshotSweeper>();

                    await sweeper.SweepAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // One failed sweep must not end the service: the next one deletes what this
                    // one did not, because the work is defined by the cutoff and not by progress
                    // kept anywhere.
                    _logger.LogError(ex, "Error during the retention sweep.");
                }

                await Task.Delay(_options.SweepInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown during one of the delays. Nothing to report.
        }

        _logger.LogInformation("Retention service stopped.");
    }
}
