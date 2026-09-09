using Microsoft.Extensions.Logging.Abstractions;
using ServerMonitor.Agent;

namespace ServerMonitor.Tests.AgentTests;

/// <summary>
/// Keeping the buffer across a restart.
/// </summary>
/// <remarks>
/// The buffer exists because the server can be unreachable. Holding it only in memory meant the
/// two failures that arrive together — the server is down, and the machine reboots — produced
/// exactly the loss the buffer was there to prevent.
/// </remarks>
public class BufferStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;
    private readonly BufferStore _store;

    public BufferStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "sm-buffer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _path = Path.Combine(_directory, "agent-buffer.json");
        _store = new BufferStore(_path, NullLogger<BufferStore>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private static MetricReport Reading(double cpu) => new()
    {
        TimestampUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        CpuUsagePercent = cpu,
        MemoryUsedMb = 4096,
        MemoryTotalMb = 8192,
        DiskUsedGb = 50,
        DiskTotalGb = 100,
        UptimeSeconds = 3600
    };

    [Fact]
    public async Task WhatIsSaved_ComesBack()
    {
        await _store.SaveAsync([Reading(10), Reading(20), Reading(30)], default);

        var loaded = await _store.LoadAsync(default);

        Assert.Equal(3, loaded.Count);
        Assert.Equal([10, 20, 30], loaded.Select(r => r.CpuUsagePercent));
    }

    [Fact]
    public async Task NoFile_LoadsAsEmpty()
    {
        Assert.Empty(await _store.LoadAsync(default));
    }

    [Fact]
    public async Task SavingNothing_RemovesTheFile()
    {
        await _store.SaveAsync([Reading(10)], default);
        Assert.True(File.Exists(_path));

        // An empty buffer has to mean no file. A stale one left behind would make the next start
        // replay readings the server already accepted.
        await _store.SaveAsync([], default);

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task ACorruptFile_IsDiscardedRatherThanThrown()
    {
        await File.WriteAllTextAsync(_path, "{ this is not json");

        var loaded = await _store.LoadAsync(default);

        // Losing an hour of buffered readings is a small harm. An agent that refuses to start
        // because of them is a machine nobody is watching, which is a much larger one.
        Assert.Empty(loaded);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task ATruncatedFile_IsDiscardedToo()
    {
        await _store.SaveAsync([Reading(10), Reading(20)], default);

        var text = await File.ReadAllTextAsync(_path);
        await File.WriteAllTextAsync(_path, text[..(text.Length / 2)]);

        Assert.Empty(await _store.LoadAsync(default));
    }

    [Fact]
    public async Task SavingTwice_LeavesNoTemporaryFileBehind()
    {
        await _store.SaveAsync([Reading(10)], default);
        await _store.SaveAsync([Reading(20), Reading(30)], default);

        // The write goes to a temporary file and is moved into place, which is what makes a crash
        // halfway through leave either the old file or the new one rather than a broken one. The
        // temporary must not survive the move.
        Assert.Equal(["agent-buffer.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));

        var loaded = await _store.LoadAsync(default);

        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public async Task Reload_KeepsTheReadingsIntact()
    {
        var original = Reading(42.5);

        await _store.SaveAsync([original], default);

        var loaded = Assert.Single(await _store.LoadAsync(default));

        Assert.Equal(original.TimestampUtc, loaded.TimestampUtc);
        Assert.Equal(original.CpuUsagePercent, loaded.CpuUsagePercent);
        Assert.Equal(original.MemoryUsedMb, loaded.MemoryUsedMb);
        Assert.Equal(original.UptimeSeconds, loaded.UptimeSeconds);
    }

    [Fact]
    public async Task RestoringIntoTheBuffer_KeepsTheNewestWithinCapacity()
    {
        await _store.SaveAsync([Reading(1), Reading(2), Reading(3), Reading(4)], default);

        var buffer = new MetricBuffer(capacity: 2);
        buffer.Restore(await _store.LoadAsync(default));

        // The setting may have been lowered since the file was written. Restoring more than the
        // limit would quietly defeat the point of having one, and the newest readings are the
        // ones worth keeping.
        Assert.Equal(2, buffer.Count);
        Assert.Equal([3, 4], buffer.Snapshot().Select(r => r.CpuUsagePercent));
    }

    [Fact]
    public void Delete_OnAMissingFile_DoesNothing()
    {
        _store.Delete();
        _store.Delete();

        Assert.False(File.Exists(_path));
    }
}
