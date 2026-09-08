using System.Globalization;
using ServerMonitor.Collection;

namespace ServerMonitor.Tests.Monitoring;

/// <summary>
/// Tests for parsing the /proc text formats. No real Linux is needed: the input is the same
/// text the kernel would hand over.
/// </summary>
public class ProcParserTests
{
    // A fragment of a real /proc/meminfo. There are many spaces between the colon and the
    // number, which is exactly why the parser uses RemoveEmptyEntries.
    private const string MemInfoSample = """
        MemTotal:       16316412 kB
        MemFree:          264132 kB
        MemAvailable:   11238904 kB
        Buffers:          198456 kB
        Cached:          9871232 kB
        """;

    private static string[] MemInfoLines => MemInfoSample.Split('\n');

    [Fact]
    public void ParseMemInfo_ReadsTotalAndAvailable()
    {
        var (totalKb, availableKb) = ProcParser.ParseMemInfo(MemInfoLines);

        Assert.Equal(16316412, totalKb);
        Assert.Equal(11238904, availableKb);
    }

    [Fact]
    public void ParseMemInfo_IgnoresMemFree()
    {
        // MemFree is deliberately unused: the kernel gives free memory to the cache, so by
        // that measure the system would always look full.
        var (_, availableKb) = ProcParser.ParseMemInfo(MemInfoLines);

        Assert.NotEqual(264132, availableKb);
    }

    [Fact]
    public void ParseMemInfo_ThrowsWhenTotalIsMissing()
    {
        var lines = new[] { "MemFree: 264132 kB" };

        var exception = Assert.Throws<FormatException>(() => ProcParser.ParseMemInfo(lines));

        Assert.Contains("MemTotal", exception.Message);
    }

    [Fact]
    public void ParseUptimeSeconds_ReadsFirstNumber()
    {
        var seconds = ProcParser.ParseUptimeSeconds("348915.42 2712334.11");

        Assert.Equal(348915.42, seconds, precision: 2);
    }

    [Fact]
    public void ParseUptimeSeconds_UsesInvariantCulture()
    {
        // A regression test for a real bug: without an explicit invariant culture, parsing
        // "348915.42" threw in cultures where the decimal separator is a comma.
        var original = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");

            var seconds = ProcParser.ParseUptimeSeconds("348915.42 2712334.11");

            Assert.Equal(348915.42, seconds, precision: 2);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseUptimeSeconds_ThrowsOnEmptyInput(string content)
    {
        Assert.Throws<FormatException>(() => ProcParser.ParseUptimeSeconds(content));
    }

    [Fact]
    public void ParseCpuTimes_SumsAllStatesIntoTotal()
    {
        var times = ProcParser.ParseCpuTimes("cpu  100 20 30 500 40 5 5 0 0 0");

        // Idle = idle + iowait = 500 + 40
        Assert.Equal(540, times.Idle);
        // Total = 100 + 20 + 30 + 500 + 40 + 5 + 5
        Assert.Equal(700, times.Total);
    }

    [Theory]
    [InlineData("cpu 1 2 3")]                    // too few fields
    [InlineData("intr 1 2 3 4 5 6 7 8")]         // the wrong line of the file
    [InlineData("")]                             // an empty line
    public void ParseCpuTimes_ThrowsOnMalformedInput(string line)
    {
        Assert.Throws<FormatException>(() => ProcParser.ParseCpuTimes(line));
    }

    [Fact]
    public void ParseCpuTimes_ThrowsOnNonNumericCounter()
    {
        Assert.Throws<FormatException>(
            () => ProcParser.ParseCpuTimes("cpu  100 20 abc 500 40 5 5"));
    }

    [Fact]
    public void CalculateCpuUsagePercent_ReturnsShareOfNonIdleTime()
    {
        var first = new CpuTimes { Idle = 95000, Total = 100000 };
        var second = new CpuTimes { Idle = 95300, Total = 100400 };

        // Over the interval: 400 ticks total, 300 of them idle, so 25% busy
        var usage = ProcParser.CalculateCpuUsagePercent(first, second);

        Assert.Equal(25, usage);
    }

    [Fact]
    public void CalculateCpuUsagePercent_ReturnsHundredWhenNeverIdle()
    {
        var first = new CpuTimes { Idle = 1000, Total = 5000 };
        var second = new CpuTimes { Idle = 1000, Total = 5400 };

        Assert.Equal(100, ProcParser.CalculateCpuUsagePercent(first, second));
    }

    [Fact]
    public void CalculateCpuUsagePercent_UsesFloatingPointDivision()
    {
        // Guards against integer division, which would always yield exactly 100%.
        var first = new CpuTimes { Idle = 0, Total = 0 };
        var second = new CpuTimes { Idle = 1, Total = 3 };

        var usage = ProcParser.CalculateCpuUsagePercent(first, second);

        Assert.Equal(66.67, usage, precision: 2);
    }

    [Fact]
    public void CalculateCpuUsagePercent_ReturnsZeroWhenCountersDidNotAdvance()
    {
        var same = new CpuTimes { Idle = 100, Total = 200 };

        Assert.Equal(0, ProcParser.CalculateCpuUsagePercent(same, same));
    }

    [Fact]
    public void CalculateCpuUsagePercent_ReturnsZeroWhenCountersWentBackwards()
    {
        var first = new CpuTimes { Idle = 500, Total = 1000 };
        var second = new CpuTimes { Idle = 100, Total = 200 };

        Assert.Equal(0, ProcParser.CalculateCpuUsagePercent(first, second));
    }

    [Fact]
    public void CalculateCpuUsagePercent_ClampsIdleGrowingFasterThanTotal()
    {
        // This happens when counters fall out of sync; a negative load is not acceptable.
        var first = new CpuTimes { Idle = 0, Total = 0 };
        var second = new CpuTimes { Idle = 200, Total = 100 };

        Assert.Equal(0, ProcParser.CalculateCpuUsagePercent(first, second));
    }
}
