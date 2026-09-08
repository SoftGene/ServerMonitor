using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Infrastructure.Retention;

/// <summary>
/// Deletes readings older than the configured lifetime.
/// </summary>
/// <remarks>
/// Separate from the background service that schedules it, for two reasons. It needs a
/// DbContext, which is scoped and cannot be injected into a singleton hosted service; and a
/// method that takes the current time as an argument can be tested in a second, while a loop
/// running every six hours cannot be tested at all.
/// </remarks>
public class SnapshotSweeper
{
    private readonly AppDbContext _dbContext;
    private readonly RetentionOptions _options;
    private readonly ILogger<SnapshotSweeper> _logger;

    public SnapshotSweeper(
        AppDbContext dbContext,
        IOptions<RetentionOptions> options,
        ILogger<SnapshotSweeper> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Removes every reading older than the retention window and returns how many rows went.
    /// </summary>
    /// <param name="nowUtc">
    /// The moment to measure the window back from. An argument rather than DateTime.UtcNow, so
    /// a test can hand it a date instead of waiting for one.
    /// </param>
    public async Task<int> SweepAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!_options.IsEnabled)
        {
            return 0;
        }

        var cutoffUtc = nowUtc - _options.SnapshotLifetime;
        var batchSize = _options.EffectiveBatchSize;
        var total = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Written by hand rather than through ExecuteDeleteAsync over a LINQ query, because
            // the batching is the point and LINQ has no way to say "delete at most N rows".
            // PostgreSQL has no DELETE ... LIMIT either, so the limit goes on a subquery that
            // picks the ids and the delete matches on those.
            //
            // No ORDER BY: every row older than the cutoff is going to be deleted eventually, so
            // which ones this batch takes does not matter, and sorting them would cost a sort.
            //
            // The interpolated values are not concatenated into the string. ExecuteSqlInterpolated
            // turns each hole into a real query parameter, which is what keeps this from being an
            // SQL injection whenever one of them stops being an int one day.
            var deleted = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 DELETE FROM "MetricSnapshots"
                 WHERE "Id" IN (
                     SELECT "Id" FROM "MetricSnapshots"
                     WHERE "TimestampUtc" < {cutoffUtc}
                     LIMIT {batchSize}
                 )
                 """,
                cancellationToken);

            total += deleted;

            // A short batch means nothing older than the cutoff is left.
            if (deleted < batchSize)
            {
                break;
            }
        }

        if (total > 0)
        {
            _logger.LogInformation(
                "Retention removed {Count} readings older than {Cutoff:u}.", total, cutoffUtc);
        }
        else
        {
            // Debug rather than information: on a healthy instance this is the usual outcome and
            // would be pure noise every six hours. It exists because "deleted nothing" and "never
            // ran" look identical in a log that only speaks when it deletes something.
            _logger.LogDebug("Retention found nothing older than {Cutoff:u}.", cutoffUtc);
        }

        return total;
    }
}
