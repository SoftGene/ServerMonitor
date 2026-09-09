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
        if (_options.IsEnabled)
        {
            _logger.LogInformation(
                "Retention service started; keeping {Days} days of readings, sweeping every {Hours} h.",
                _options.SnapshotDays,
                _options.SweepInterval.TotalHours);
        }
        else
        {
            // Said out loud on purpose. A system that quietly keeps everything forever looks
            // exactly like a system with a retention policy, right up until the disk fills.
            //
            // The service keeps running regardless: summarising the hours is worth doing whether
            // or not anything is being deleted, and switching deletion off should not silently
            // switch off the long-term history as well.
            _logger.LogWarning(
                "Retention is disabled (Retention:SnapshotDays is {Days}); raw readings are kept "
                + "forever, and only the hourly summaries are still being built.",
                _options.SnapshotDays);
        }

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // One failed pass must not end the service: the next one does what this one
                    // did not, because the work is defined by the cutoff and by which hours are
                    // already summarised, not by progress kept anywhere.
                    _logger.LogError(ex, "Error during the retention pass.");
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

    /// <summary>
    /// One pass: summarise the finished hours, then delete what has aged out.
    /// </summary>
    /// <remarks>
    /// The order is the whole design and it is not interchangeable. Deleting first destroys the
    /// readings the summary would have been built from, and it does so without any sign of
    /// trouble: rows that were never summarised delete exactly as successfully as rows that were.
    /// A single scope covers both, so the summary is committed before the delete is even attempted.
    /// </remarks>
    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();

        var rollups = scope.ServiceProvider.GetRequiredService<RollupBuilder>();
        await rollups.BuildAsync(nowUtc, cancellationToken);

        if (!_options.IsEnabled)
        {
            return;
        }

        var sweeper = scope.ServiceProvider.GetRequiredService<SnapshotSweeper>();
        await sweeper.SweepAsync(nowUtc, cancellationToken);
    }
}
