using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Tests.Domain;

public class MetricSnapshotTests
{
    [Fact]
    public void MemoryUsagePercent_IsCalculatedFromAbsoluteValues()
    {
        var snapshot = new MetricSnapshot { MemoryUsedMb = 8192, MemoryTotalMb = 16384 };

        Assert.Equal(50, snapshot.MemoryUsagePercent);
    }

    [Fact]
    public void DiskUsagePercent_IsCalculatedFromAbsoluteValues()
    {
        var snapshot = new MetricSnapshot { DiskUsedGb = 250, DiskTotalGb = 1000 };

        Assert.Equal(25, snapshot.DiskUsagePercent);
    }

    [Fact]
    public void UsagePercent_IsRoundedToOneDecimal()
    {
        var snapshot = new MetricSnapshot { MemoryUsedMb = 1, MemoryTotalMb = 3 };

        Assert.Equal(33.3, snapshot.MemoryUsagePercent);
    }

    [Fact]
    public void UsagePercent_ReturnsZeroWhenTotalIsUnknown()
    {
        // Деления на ноль быть не должно: до первого успешного замера тотал равен нулю.
        var snapshot = new MetricSnapshot { MemoryUsedMb = 100, MemoryTotalMb = 0, DiskUsedGb = 5, DiskTotalGb = 0 };

        Assert.Equal(0, snapshot.MemoryUsagePercent);
        Assert.Equal(0, snapshot.DiskUsagePercent);
    }
}

public class MetricKindExtensionsTests
{
    [Theory]
    [InlineData(MetricKind.Cpu, "CPU")]
    [InlineData(MetricKind.Memory, "Memory")]
    [InlineData(MetricKind.Disk, "Disk")]
    [InlineData(MetricKind.Availability, "Availability")]
    public void ToDisplayName_MatchesTheFormatStoredInTheDatabase(MetricKind kind, string expected)
    {
        // Эти же строки лежат в колонке Alerts.MetricType, включая записи,
        // сделанные до перехода на перечисления.
        Assert.Equal(expected, kind.ToDisplayName());
    }

    [Fact]
    public void EveryKind_HasADisplayName()
    {
        // Страховка на будущее: добавив значение в перечисление, легко забыть про строку,
        // под которой оно ляжет в базу. Тест ловит пустое или дефолтное имя.
        foreach (var kind in Enum.GetValues<MetricKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(kind.ToDisplayName()));
        }
    }
}
