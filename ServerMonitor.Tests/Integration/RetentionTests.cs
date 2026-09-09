using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;
using ServerMonitor.Infrastructure.Retention;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// Deleting is the one operation nothing undoes, so what it spares matters as much as what it
/// removes. Every test here asserts both halves.
/// </summary>
[Collection(ApiCollection.Name)]
public class RetentionTests : IAsyncLifetime
{
    private readonly ApiFixture _api;

    public RetentionTests(ApiFixture api)
    {
        _api = api;
    }

    public Task InitializeAsync() => _api.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Creates a machine and gives it readings at the requested ages.</summary>
    private Task<int> SeedAsync(params TimeSpan[] ages) => _api.WithDbContextAsync(async db =>
    {
        var server = new Server
        {
            PublicId = Guid.NewGuid(),
            Name = "retention-subject",
            RegisteredAtUtc = Now.AddYears(-1)
        };

        db.Servers.Add(server);
        await db.SaveChangesAsync();

        db.MetricSnapshots.AddRange(ages.Select(age => new MetricSnapshot
        {
            ServerId = server.Id,
            TimestampUtc = Now - age,
            CpuUsagePercent = 10,
            MemoryUsedMb = 1,
            MemoryTotalMb = 2,
            DiskUsedGb = 1,
            DiskTotalGb = 2,
            UptimeSeconds = 1
        }));

        await db.SaveChangesAsync();

        return server.Id;
    });

    private Task<int> SweepAsync() =>
        _api.WithServiceAsync<SnapshotSweeper, int>(sweeper => sweeper.SweepAsync(Now));

    private Task<List<DateTime>> RemainingAsync() => _api.WithDbContextAsync(db =>
        db.MetricSnapshots.AsNoTracking().Select(m => m.TimestampUtc).ToListAsync());

    [Fact]
    public async Task Sweep_RemovesWhatIsOlderThanTheWindowAndKeepsTheRest()
    {
        // The window is seven days. Two of these fall outside it, two inside.
        await SeedAsync(
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(8),
            TimeSpan.FromDays(6),
            TimeSpan.FromMinutes(5));

        var deleted = await SweepAsync();

        Assert.Equal(2, deleted);

        var remaining = await RemainingAsync();

        Assert.Equal(2, remaining.Count);
        Assert.All(remaining, timestamp => Assert.True(timestamp > Now.AddDays(-7)));
    }

    [Fact]
    public async Task Sweep_KeepsAReadingExactlyOnTheBoundary()
    {
        // The comparison is strictly "older than", so the row sitting precisely on the cutoff
        // survives. Worth pinning down: whether a boundary is inclusive is exactly the kind of
        // decision that gets flipped by accident during a refactor, and here a flip deletes data.
        await SeedAsync(TimeSpan.FromDays(7));

        var deleted = await SweepAsync();

        Assert.Equal(0, deleted);
        Assert.Single(await RemainingAsync());
    }

    [Fact]
    public async Task Sweep_DeletesInBatchesUntilNothingOldIsLeft()
    {
        // The batch size is 100, so 250 old rows take three statements. If the loop stopped after
        // the first batch the test would see 150 rows left and the bug would be obvious — which
        // it would not be in production, where the count merely stops falling.
        var ages = Enumerable.Range(0, 250)
            .Select(i => TimeSpan.FromDays(10) + TimeSpan.FromMinutes(i))
            .ToArray();

        await SeedAsync(ages);

        var deleted = await SweepAsync();

        Assert.Equal(250, deleted);
        Assert.Empty(await RemainingAsync());
    }

    [Fact]
    public async Task Sweep_LeavesRecentReadingsAloneAndReportsNothing()
    {
        await SeedAsync(TimeSpan.FromHours(1), TimeSpan.FromDays(2));

        var deleted = await SweepAsync();

        Assert.Equal(0, deleted);
        Assert.Equal(2, (await RemainingAsync()).Count);
    }

    [Fact]
    public async Task Sweep_DeletesNothingWhenRetentionIsDisabled()
    {
        // The safety rule, and the reason this test exists at all: a retention of zero days must
        // read as "keep everything", never as "everything is older than zero days, delete it".
        // Getting that backwards empties the database on the first sweep after a typo.
        await SeedAsync(TimeSpan.FromDays(365));

        var deleted = await _api.WithServiceAsync<AppDbContext, int>(db =>
        {
            var sweeper = new SnapshotSweeper(
                db,
                Options.Create(new RetentionOptions { SnapshotDays = 0 }),
                NullLogger<SnapshotSweeper>.Instance);

            return sweeper.SweepAsync(Now);
        });

        Assert.Equal(0, deleted);
        Assert.Single(await RemainingAsync());
    }

    [Fact]
    public async Task Sweep_DoesNotTouchTheMachinesThemselves()
    {
        // Deleting a machine's last reading must not look like deleting the machine. It stays in
        // the fleet, reported as offline, which is a true statement about it.
        await SeedAsync(TimeSpan.FromDays(30));

        await SweepAsync();

        var servers = await _api.WithDbContextAsync(db =>
            db.Servers.AsNoTracking().CountAsync());

        Assert.Equal(1, servers);
    }
}
