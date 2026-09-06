using ServerMonitor.Agent;

// Не ServerMonitor.Tests.Agent: такое имя затеняло бы пространство имён самого агента.
namespace ServerMonitor.Tests.AgentTests;

public class MetricBufferTests
{
    private static MetricReport Reading(double cpu) => new() { CpuUsagePercent = cpu };

    [Fact]
    public void Add_KeepsItemsInOrder()
    {
        var buffer = new MetricBuffer(capacity: 10);

        buffer.Add(Reading(1));
        buffer.Add(Reading(2));

        Assert.Equal(new double[] { 1, 2 }, buffer.Snapshot().Select(r => r.CpuUsagePercent));
    }

    [Fact]
    public void Add_DropsOldestWhenFull()
    {
        var buffer = new MetricBuffer(capacity: 2);

        buffer.Add(Reading(1));
        buffer.Add(Reading(2));
        buffer.Add(Reading(3));

        // Свежие данные ценнее исторических: выбрасывается самый старый замер.
        Assert.Equal(2, buffer.Count);
        Assert.Equal(new double[] { 2, 3 }, buffer.Snapshot().Select(r => r.CpuUsagePercent));
    }

    [Fact]
    public void Remove_DropsSentItemsFromTheFront()
    {
        var buffer = new MetricBuffer(capacity: 10);
        buffer.Add(Reading(1));
        buffer.Add(Reading(2));
        buffer.Add(Reading(3));

        buffer.Remove(2);

        Assert.Equal(new double[] { 3 }, buffer.Snapshot().Select(r => r.CpuUsagePercent));
    }

    [Fact]
    public void Remove_IgnoresCountLargerThanBuffer()
    {
        var buffer = new MetricBuffer(capacity: 10);
        buffer.Add(Reading(1));

        buffer.Remove(5);

        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Remove_OnlyDropsWhatWasSent()
    {
        // Пока запрос летел, цикл успел добавить новый замер — его терять нельзя.
        var buffer = new MetricBuffer(capacity: 10);
        buffer.Add(Reading(1));
        buffer.Add(Reading(2));

        var sent = buffer.Snapshot();
        buffer.Add(Reading(3));
        buffer.Remove(sent.Count);

        Assert.Equal(new double[] { 3 }, buffer.Snapshot().Select(r => r.CpuUsagePercent));
    }

    [Fact]
    public void Capacity_IsAtLeastOne()
    {
        // Ноль в конфигурации не должен превращать буфер в чёрную дыру.
        var buffer = new MetricBuffer(capacity: 0);

        buffer.Add(Reading(1));

        Assert.Equal(1, buffer.Count);
    }
}
