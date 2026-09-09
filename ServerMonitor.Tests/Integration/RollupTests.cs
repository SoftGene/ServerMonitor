using Microsoft.EntityFrameworkCore;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Retention;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// The aggregation is one SQL statement, so there is nothing here a unit test could reach: the
/// bucketing, the grouping, the division and the upsert all happen inside PostgreSQL.
/// </summary>
[Collection(ApiCollection.Name)]
public class RollupTests : IAsyncLifetime
{
    private readonly ApiFixture _api;

    public RollupTests(ApiFixture api)
    {
        _api = api;
    }

    public Task InitializeAsync() => _api.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // A fixed "now" on an exact hour boundary, so every age in these tests is unambiguous.
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private Task<int> SeedAsync(string name, params (DateTime At, double Cpu, double MemUsed, double MemTotal)[] readings) =>
        _api.WithDbContextAsync(async db =>
        {
            var server = new Server
            {
                PublicId = Guid.NewGuid(),
                Name = name,
                RegisteredAtUtc = Now.AddYears(-1)
            };

            db.Servers.Add(server);
            await db.SaveChangesAsync();

            db.MetricSnapshots.AddRange(readings.Select(r => new MetricSnapshot
            {
                ServerId = server.Id,
                TimestampUtc = r.At,
                CpuUsagePercent = r.Cpu,
                MemoryUsedMb = r.MemUsed,
                MemoryTotalMb = r.MemTotal,
                DiskUsedGb = 50,
                DiskTotalGb = 100,
                UptimeSeconds = 1
            }));

            await db.SaveChangesAsync();

            return server.Id;
        });

    private Task<int> BuildAsync(DateTime? nowUtc = null) =>
        _api.WithServiceAsync<RollupBuilder, int>(builder => builder.BuildAsync(nowUtc ?? Now));

    private Task<List<MetricRollup>> RollupsAsync() => _api.WithDbContextAsync(db =>
        db.MetricRollups.AsNoTracking().OrderBy(r => r.HourUtc).ToListAsync());

    [Fact]
    public async Task Build_AveragesAndPeaksTheReadingsOfEachHour()
    {
        var hour = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

        await SeedAsync("averages",
            (hour.AddMinutes(1), 10, 2048, 8192),
            (hour.AddMinutes(2), 30, 4096, 8192),
            (hour.AddMinutes(3), 80, 6144, 8192));

        var written = await BuildAsync();

        Assert.Equal(1, written);

        var rollup = Assert.Single(await RollupsAsync());

        Assert.Equal(hour, rollup.HourUtc);
        Assert.Equal(3, rollup.SampleCount);
        Assert.Equal(40, rollup.CpuAvgPercent, 3);
        Assert.Equal(80, rollup.CpuMaxPercent, 3);

        // 25%, 50%, 75% — the division is done in SQL, because MemoryUsagePercent is a C#
        // property with no column behind it.
        Assert.Equal(50, rollup.MemoryAvgPercent, 3);
        Assert.Equal(75, rollup.MemoryMaxPercent, 3);
    }

    [Fact]
    public async Task Build_PutsEachHourInItsOwnRow()
    {
        await SeedAsync("hours",
            (new DateTime(2026, 6, 1, 8, 59, 59, DateTimeKind.Utc), 10, 1, 2),
            (new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), 20, 1, 2),
            (new DateTime(2026, 6, 1, 9, 59, 59, DateTimeKind.Utc), 30, 1, 2),
            (new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc), 40, 1, 2));

        await BuildAsync();

        var rollups = await RollupsAsync();

        // Three hours, and the reading one second before ten belongs to the nine o'clock hour.
        Assert.Equal(3, rollups.Count);
        Assert.Equal([8, 9, 10], rollups.Select(r => r.HourUtc.Hour));
        Assert.Equal([1, 2, 1], rollups.Select(r => r.SampleCount));
    }

    [Fact]
    public async Task Build_LeavesTheHourInProgressAlone()
    {
        // Now is 12:00 exactly, so 11:xx is finished and 12:xx is not.
        await SeedAsync("in-progress",
            (new DateTime(2026, 6, 1, 11, 30, 0, DateTimeKind.Utc), 10, 1, 2),
            (new DateTime(2026, 6, 1, 12, 30, 0, DateTimeKind.Utc), 90, 1, 2));

        await BuildAsync(new DateTime(2026, 6, 1, 12, 45, 0, DateTimeKind.Utc));

        var rollup = Assert.Single(await RollupsAsync());

        // Averaging a fraction of an hour would publish a number nobody meant, and it would
        // change every time the pass ran.
        Assert.Equal(11, rollup.HourUtc.Hour);
    }

    [Fact]
    public async Task Build_KeepsMachinesApart()
    {
        var hour = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

        await SeedAsync("machine-one", (hour.AddMinutes(5), 10, 1, 2));
        await SeedAsync("machine-two", (hour.AddMinutes(5), 90, 1, 2));

        await BuildAsync();

        var rollups = await RollupsAsync();

        // Same hour, two machines: grouping by hour alone would average them into one useless row.
        Assert.Equal(2, rollups.Count);
        Assert.Equal([10, 90], rollups.OrderBy(r => r.CpuAvgPercent).Select(r => r.CpuAvgPercent));
    }

    [Fact]
    public async Task Build_RunTwice_DoesNotDuplicateAnHour()
    {
        var hour = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

        await SeedAsync("idempotent", (hour.AddMinutes(5), 42, 1, 2));

        await BuildAsync();
        await BuildAsync();

        // The unique index plus ON CONFLICT is what makes a repeated pass an update. Without the
        // index PostgreSQL would refuse the statement; without ON CONFLICT it would insert twice.
        var rollup = Assert.Single(await RollupsAsync());

        Assert.Equal(42, rollup.CpuAvgPercent, 3);
    }

    [Fact]
    public async Task Build_RefreshesAnHourThatGainedMoreReadings()
    {
        var hour = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var serverId = await SeedAsync("late-arrival", (hour.AddMinutes(5), 10, 1, 2));

        await BuildAsync();

        // An agent that buffered through an outage delivers its backlog after the hour is over.
        await _api.WithDbContextAsync(async db =>
        {
            db.MetricSnapshots.Add(new MetricSnapshot
            {
                ServerId = serverId,
                TimestampUtc = hour.AddMinutes(50),
                CpuUsagePercent = 90,
                MemoryUsedMb = 1,
                MemoryTotalMb = 2,
                DiskUsedGb = 50,
                DiskTotalGb = 100,
                UptimeSeconds = 1
            });

            return await db.SaveChangesAsync();
        });

        await BuildAsync();

        var rollup = Assert.Single(await RollupsAsync());

        Assert.Equal(2, rollup.SampleCount);
        Assert.Equal(50, rollup.CpuAvgPercent, 3);
    }

    [Fact]
    public async Task Build_WillNotOverwriteASummaryWithAThinnerOne()
    {
        var hour = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var serverId = await SeedAsync("thinner",
            (hour.AddMinutes(10), 10, 1, 2),
            (hour.AddMinutes(20), 20, 1, 2),
            (hour.AddMinutes(30), 60, 1, 2));

        await BuildAsync();

        // Retention deletes the raw readings behind that hour, leaving one behind.
        await _api.WithDbContextAsync(db => db.MetricSnapshots
            .Where(m => m.ServerId == serverId && m.CpuUsagePercent != 10)
            .ExecuteDeleteAsync());

        await BuildAsync();

        var rollup = Assert.Single(await RollupsAsync());

        // Without the WHERE on DO UPDATE the summary would now claim this hour averaged 10%,
        // computed from the single row that happened to survive. The good summary stands.
        Assert.Equal(3, rollup.SampleCount);
        Assert.Equal(30, rollup.CpuAvgPercent, 3);
    }

    [Fact]
    public async Task Build_SurvivesAReadingWithNoTotal()
    {
        var hour = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

        // A machine that reported a total of zero — a container with no memory limit reported it
        // this way once. Dividing by it would fail the whole statement and stop every summary,
        // not just this one.
        await SeedAsync("no-total",
            (hour.AddMinutes(5), 10, 0, 0),
            (hour.AddMinutes(6), 20, 4096, 8192));

        await BuildAsync();

        var rollup = Assert.Single(await RollupsAsync());

        Assert.Equal(2, rollup.SampleCount);
        // The unusable reading is skipped by the average rather than counted as zero percent,
        // which would drag the hour down with a number that was never measured.
        Assert.Equal(50, rollup.MemoryAvgPercent, 3);
    }

    [Fact]
    public async Task Build_DoesNothingWithoutReadings()
    {
        var written = await BuildAsync();

        Assert.Equal(0, written);
        Assert.Empty(await RollupsAsync());
    }

    [Fact]
    public async Task DeletingAServer_TakesItsSummariesWithIt()
    {
        var hour = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        await SeedAsync("doomed", (hour.AddMinutes(5), 10, 1, 2));

        await BuildAsync();
        Assert.NotEmpty(await RollupsAsync());

        await _api.WithDbContextAsync(db => db.Servers.ExecuteDeleteAsync());

        Assert.Empty(await RollupsAsync());
    }
}
