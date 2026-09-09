using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Infrastructure.Retention;

/// <summary>
/// Reduces complete hours of raw readings to one row each, before retention deletes them.
/// </summary>
/// <remarks>
/// The order matters more than anything else in this class: aggregate first, delete second. The
/// other way round is not a slower version of the same thing — it is permanent data loss, and it
/// would look completely healthy in the log, because deleting rows that were never summarised
/// reports exactly the same success as deleting rows that were.
/// </remarks>
public class RollupBuilder
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<RollupBuilder> _logger;

    public RollupBuilder(AppDbContext dbContext, ILogger<RollupBuilder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Summarises every complete hour that has readings and is not already summarised, and
    /// returns how many hourly rows were written or refreshed.
    /// </summary>
    /// <param name="nowUtc">
    /// The current moment. Everything strictly before the top of this hour is treated as
    /// finished; the hour in progress is left alone, because an average over a fraction of an
    /// hour is not the number anyone means by "that hour".
    /// </param>
    public async Task<int> BuildAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var currentHourUtc = new DateTime(
            nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);

        var fromUtc = await ResolveStartAsync(cancellationToken);

        if (fromUtc is null || fromUtc >= currentHourUtc)
        {
            return 0;
        }

        // One statement does the whole job: group the raw rows into hours, and insert each group
        // as a row — updating instead of inserting where that hour is already present.
        //
        // date_trunc is PostgreSQL's hour bucketing. Doing it in the database rather than pulling
        // the rows into C# is the entire point: a month of readings for three machines is two
        // million rows to transfer and nothing to show for it, against a few thousand rows of
        // result computed where the data already is.
        //
        // The third argument names the time zone, and leaving it out would be a real bug rather
        // than a stylistic choice. These columns are "timestamp with time zone", and the two-
        // argument date_trunc truncates such a value in whatever zone the session happens to be
        // set to — so the same reading would land in a different hour depending on the server's
        // configuration, and in a zone offset by thirty or forty-five minutes it would land on a
        // boundary that is not an hour at all.
        //
        // The percentages are divided out by hand. MetricSnapshot exposes MemoryUsagePercent as a
        // C# property, but this projection runs on the database side and a computed property has
        // no SQL to translate to — the same note is on the history endpoint.
        //
        // NULLIF guards the division: a reading that arrived with a total of zero would otherwise
        // divide by zero. NULLIF turns the zero into NULL, the division yields NULL, and avg()
        // skips NULLs rather than poisoning the whole hour. COALESCE then keeps the stored column
        // non-null in the case where every reading in the hour was like that.
        //
        // The WHERE on DO UPDATE is the safety catch. Re-running over an hour whose raw rows have
        // already been deleted would otherwise overwrite a good summary with one computed from
        // whatever fragment survives. An aggregate built from fewer samples never replaces one
        // built from more.
        var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "MetricRollups" (
                 "ServerId", "HourUtc", "SampleCount",
                 "CpuAvgPercent", "CpuMaxPercent",
                 "MemoryAvgPercent", "MemoryMaxPercent",
                 "DiskAvgPercent", "DiskMaxPercent")
             SELECT
                 "ServerId",
                 date_trunc('hour', "TimestampUtc", 'UTC') AS hour_utc,
                 count(*),
                 avg("CpuUsagePercent"),
                 max("CpuUsagePercent"),
                 coalesce(avg("MemoryUsedMb" / nullif("MemoryTotalMb", 0) * 100), 0),
                 coalesce(max("MemoryUsedMb" / nullif("MemoryTotalMb", 0) * 100), 0),
                 coalesce(avg("DiskUsedGb" / nullif("DiskTotalGb", 0) * 100), 0),
                 coalesce(max("DiskUsedGb" / nullif("DiskTotalGb", 0) * 100), 0)
             FROM "MetricSnapshots"
             WHERE "TimestampUtc" >= {fromUtc} AND "TimestampUtc" < {currentHourUtc}
             GROUP BY "ServerId", date_trunc('hour', "TimestampUtc", 'UTC')
             ON CONFLICT ("ServerId", "HourUtc") DO UPDATE SET
                 "SampleCount" = EXCLUDED."SampleCount",
                 "CpuAvgPercent" = EXCLUDED."CpuAvgPercent",
                 "CpuMaxPercent" = EXCLUDED."CpuMaxPercent",
                 "MemoryAvgPercent" = EXCLUDED."MemoryAvgPercent",
                 "MemoryMaxPercent" = EXCLUDED."MemoryMaxPercent",
                 "DiskAvgPercent" = EXCLUDED."DiskAvgPercent",
                 "DiskMaxPercent" = EXCLUDED."DiskMaxPercent"
             WHERE EXCLUDED."SampleCount" >= "MetricRollups"."SampleCount"
             """,
            cancellationToken);

        if (affected > 0)
        {
            _logger.LogInformation(
                "Rolled up {Count} hours of readings covering {From:u} to {To:u}.",
                affected, fromUtc, currentHourUtc);
        }

        return affected;
    }

    /// <summary>
    /// Where this pass should start reading.
    /// </summary>
    /// <remarks>
    /// The last hour already summarised, rather than the one after it. Redoing one hour on every
    /// pass costs almost nothing and repairs the case that would otherwise be permanent: a pass
    /// interrupted partway through an hour leaves a summary of half of it, and nothing would ever
    /// come back to correct it.
    /// <para>
    /// With no summaries at all yet, it starts at the oldest reading there is, which is what makes
    /// the feature work on a database that has been collecting since before this existed.
    /// </para>
    /// </remarks>
    private async Task<DateTime?> ResolveStartAsync(CancellationToken cancellationToken)
    {
        var lastRollup = await _dbContext.MetricRollups
            .AsNoTracking()
            .OrderByDescending(r => r.HourUtc)
            .Select(r => (DateTime?)r.HourUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastRollup is not null)
        {
            return lastRollup;
        }

        var oldestSnapshot = await _dbContext.MetricSnapshots
            .AsNoTracking()
            .OrderBy(m => m.TimestampUtc)
            .Select(m => (DateTime?)m.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (oldestSnapshot is null)
        {
            return null;
        }

        var oldest = oldestSnapshot.Value;

        return new DateTime(oldest.Year, oldest.Month, oldest.Day, oldest.Hour, 0, 0, DateTimeKind.Utc);
    }
}
