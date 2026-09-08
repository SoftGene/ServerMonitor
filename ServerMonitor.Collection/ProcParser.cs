using System.Globalization;

namespace ServerMonitor.Collection;

/// <summary>
/// Parsing of the Linux /proc text formats and the CPU usage calculation.
/// <para>
/// Nothing here touches the file system: the already-read text is passed in. That is what
/// makes these functions testable without a Linux machine at hand.
/// </para>
/// <para>
/// Every number is parsed with <see cref="CultureInfo.InvariantCulture"/>: the contents of
/// /proc are a machine format with a dot for the decimal separator and do not depend on the
/// system language. Without stating the culture explicitly, a process running under, say,
/// ru-RU would fail to parse "348915.42".
/// </para>
/// </summary>
public static class ProcParser
{
    /// <summary>Parses the contents of /proc/meminfo. Values are returned in kilobytes.</summary>
    public static (double TotalKb, double AvailableKb) ParseMemInfo(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        double totalKb = 0;
        double availableKb = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                totalKb = ParseMemInfoValue(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                availableKb = ParseMemInfoValue(line);
            }
        }

        if (totalKb <= 0)
        {
            throw new FormatException("/proc/meminfo does not contain a usable MemTotal value.");
        }

        return (totalKb, availableKb);
    }

    /// <summary>Parses the first number of /proc/uptime — seconds since the system booted.</summary>
    public static double ParseUptimeSeconds(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new FormatException("/proc/uptime is empty.");
        }

        var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            throw new FormatException($"/proc/uptime has an unexpected format: '{content.Trim()}'.");
        }

        return double.Parse(parts[0], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses the aggregate "cpu ..." line of /proc/stat.
    /// Time spent waiting on disk (iowait) counts as idle, which is what top does too.
    /// </summary>
    public static CpuTimes ParseCpuTimes(string statLine)
    {
        if (string.IsNullOrWhiteSpace(statLine))
        {
            throw new FormatException("/proc/stat cpu line is empty.");
        }

        var parts = statLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // cpu user nice system idle iowait irq softirq — at least 8 fields
        if (parts.Length < 8 || !parts[0].StartsWith("cpu", StringComparison.Ordinal))
        {
            throw new FormatException($"/proc/stat cpu line has an unexpected format: '{statLine.Trim()}'.");
        }

        long user = ParseCounter(parts[1], statLine);
        long nice = ParseCounter(parts[2], statLine);
        long system = ParseCounter(parts[3], statLine);
        long idle = ParseCounter(parts[4], statLine);
        long iowait = ParseCounter(parts[5], statLine);
        long irq = ParseCounter(parts[6], statLine);
        long softirq = ParseCounter(parts[7], statLine);

        return new CpuTimes
        {
            Idle = idle + iowait,
            Total = user + nice + system + idle + iowait + irq + softirq
        };
    }

    /// <summary>
    /// Calculates CPU usage as a percentage from two counter readings.
    /// Works for Linux (/proc/stat) and Windows (GetSystemTimes) alike — the counters have the
    /// same shape: accumulated idle time and accumulated total time.
    /// </summary>
    public static double CalculateCpuUsagePercent(CpuTimes first, CpuTimes second)
    {
        var totalDelta = second.Total - first.Total;
        var idleDelta = second.Idle - first.Idle;

        // The counters did not move, or went backwards because the source restarted. Zero is
        // a more honest answer than a division by zero or a negative load.
        if (totalDelta <= 0)
        {
            return 0;
        }

        // The cast to double is required: integer division would yield 0 here, and the usage
        // would always come out as exactly 100%.
        var usage = (1.0 - (double)idleDelta / totalDelta) * 100.0;

        return Math.Round(Math.Clamp(usage, 0, 100), 2);
    }

    private static double ParseMemInfoValue(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            throw new FormatException($"Unexpected /proc/meminfo line: '{line.Trim()}'.");
        }

        return double.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    private static long ParseCounter(string value, string sourceLine)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new FormatException($"Unexpected counter '{value}' in /proc/stat line: '{sourceLine.Trim()}'.");
        }

        return result;
    }
}
