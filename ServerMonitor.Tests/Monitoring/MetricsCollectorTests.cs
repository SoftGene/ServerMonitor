using System.Runtime.InteropServices;
using ServerMonitor.Collection;

namespace ServerMonitor.Tests.Monitoring;

/// <summary>
/// Проверка реального сбора на текущей машине. Это уже не юнит-тест: он обращается к
/// операционной системе, поэтому проверяет не точные числа, а правдоподобность результата.
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
            return; // на других платформах сбор не поддерживается by design
        }

        var collector = new MetricsCollector();

        var snapshot = await collector.CollectAsync(CancellationToken.None);

        Assert.InRange(snapshot.CpuUsagePercent, 0, 100);

        Assert.True(snapshot.MemoryTotalMb > 0, "Общий объём памяти должен быть больше нуля.");
        Assert.InRange(snapshot.MemoryUsedMb, 0, snapshot.MemoryTotalMb);

        Assert.True(snapshot.DiskTotalGb > 0, "Объём системного диска должен быть больше нуля.");
        Assert.InRange(snapshot.DiskUsedGb, 0, snapshot.DiskTotalGb);

        Assert.True(snapshot.UptimeSeconds > 0, "Время работы системы должно быть больше нуля.");

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

        // Загрузку процессора нельзя измерить мгновенно — нужен интервал между замерами
        // счётчиков. Именно из-за этой секунды реальный шаг сбора ≈ 6 секунд, а не 5.
        Assert.True(
            elapsed >= TimeSpan.FromMilliseconds(900),
            $"Ожидалась пауза около секунды на замер CPU, фактически {elapsed.TotalMilliseconds:F0} мс.");
    }
}
