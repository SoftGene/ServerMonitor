using System.Runtime.InteropServices;
using ServerMonitor.Collection;

namespace ServerMonitor.Tests.Monitoring;

/// <summary>
/// Checks real collection on the current machine. This is no longer a unit test: it talks to
/// the operating system, so it asserts that the result is plausible rather than exact.
/// </summary>
public class MetricsCollectorTests
{
    private static bool IsSupportedPlatform =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    [LocalCollectorFact]
    public async Task CollectAsync_ReturnsPlausibleValues()
    {
        if (!IsSupportedPlatform)
        {
            return; // collection is not supported on other platforms by design
        }

        var collector = new MetricsCollector();

        var snapshot = await collector.CollectAsync(CancellationToken.None);

        Assert.InRange(snapshot.CpuUsagePercent, 0, 100);

        Assert.True(snapshot.MemoryTotalMb > 0, "Total memory must be greater than zero.");
        Assert.InRange(snapshot.MemoryUsedMb, 0, snapshot.MemoryTotalMb);

        Assert.True(snapshot.DiskTotalGb > 0, "The system drive size must be greater than zero.");
        Assert.InRange(snapshot.DiskUsedGb, 0, snapshot.DiskTotalGb);

        Assert.True(snapshot.UptimeSeconds > 0, "System uptime must be greater than zero.");

        Assert.InRange(snapshot.TimestampUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [LocalCollectorFact]
    public async Task CollectAsync_TakesAboutOneSecondBecauseOfCpuSampling()
    {
        if (!IsSupportedPlatform)
        {
            return;
        }

        var collector = new MetricsCollector();

        var startedAt = DateTime.UtcNow;
        await collector.CollectAsync(CancellationToken.None);
        var elapsed = DateTime.UtcNow - startedAt;

        // CPU usage cannot be measured instantaneously — it needs a gap between two counter
        // readings. That second is why the real collection step is about 6 seconds, not 5.
        Assert.True(
            elapsed >= TimeSpan.FromMilliseconds(900),
            $"Expected about a second for the CPU sample, got {elapsed.TotalMilliseconds:F0} ms.");
    }
}
