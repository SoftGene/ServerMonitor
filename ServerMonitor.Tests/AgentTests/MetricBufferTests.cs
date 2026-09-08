using ServerMonitor.Agent;

// Not ServerMonitor.Tests.Agent: that name would shadow the agent's own namespace.
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

        // Fresh data is worth more than old: the oldest reading is the one discarded.
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
        // The loop added a new reading while the request was in flight — it must not be lost.
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
        // A zero in the configuration must not turn the buffer into a black hole.
        var buffer = new MetricBuffer(capacity: 0);

        buffer.Add(Reading(1));

        Assert.Equal(1, buffer.Count);
    }
}
