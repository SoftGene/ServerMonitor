using System.Runtime.InteropServices;
using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Monitoring;

public class MetricsCollector : IMetricsCollector
{
    public async Task<MetricSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new MetricSnapshot
        {
            TimestampUtc = DateTime.UtcNow
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await CollectLinuxMetricsAsync(snapshot, cancellationToken);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await CollectWindowsMetricsAsync(snapshot, cancellationToken);
        }
        else
        {
            throw new PlatformNotSupportedException("Only Linux and Windows are supported.");
        }

        CollectDiskMetrics(snapshot);

        return snapshot;
    }

    private async Task CollectLinuxMetricsAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        await CollectLinuxMemoryAsync(snapshot, cancellationToken);
        await CollectLinuxUptimeAsync(snapshot, cancellationToken);
        await CollectLinuxCpuAsync(snapshot, cancellationToken);
    }

    private async Task CollectLinuxMemoryAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken);

        double totalKb = 0;
        double availableKb = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("MemTotal:"))
            {
                totalKb = ParseMemInfoLine(line);
            }
            else if (line.StartsWith("MemAvailable:"))
            {
                availableKb = ParseMemInfoLine(line);
            }
        }

        var usedKb = totalKb - availableKb;

        snapshot.MemoryTotalMb = totalKb / 1024.0;
        snapshot.MemoryUsedMb = usedKb / 1024.0;
    }

    private static double ParseMemInfoLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return double.Parse(parts[1]);
    }

    private async Task CollectLinuxUptimeAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync("/proc/uptime", cancellationToken);

        var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        snapshot.UptimeSeconds = double.Parse(parts[0]);
    }

    private async Task CollectLinuxCpuAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        var first = await ReadCpuTimesAsync(cancellationToken);

        await Task.Delay(1000, cancellationToken);

        var second = await ReadCpuTimesAsync(cancellationToken);

        var totalDelta = second.Total - first.Total;
        var idleDelta = second.Idle - first.Idle;

        if (totalDelta <= 0)
        {
            snapshot.CpuUsagePercent = 0;
            return;
        }

        var usage = (1.0 - (double)idleDelta / totalDelta) * 100.0;
        snapshot.CpuUsagePercent = Math.Round(usage, 2);
    }

    private async Task<CpuTimes> ReadCpuTimesAsync(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);
        var cpuLine = lines[0];

        var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        long user = long.Parse(parts[1]);
        long nice = long.Parse(parts[2]);
        long system = long.Parse(parts[3]);
        long idle = long.Parse(parts[4]);
        long iowait = long.Parse(parts[5]);
        long irq = long.Parse(parts[6]);
        long softirq = long.Parse(parts[7]);

        long idleTotal = idle + iowait;
        long total = user + nice + system + idle + iowait + irq + softirq;

        return new CpuTimes { Idle = idleTotal, Total = total };
    }
    private readonly struct CpuTimes
    {
        public long Idle { get; init; }
        public long Total { get; init; }
    }

    private static void CollectDiskMetrics(MetricSnapshot snapshot)
    {
        DriveInfo? targetDrive = null;

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (drive.Name == "/")
                {
                    targetDrive = drive;
                    break;
                }
            }
            else
            {
                targetDrive = drive;
                break;
            }
        }

        if (targetDrive is null)
        {
            return;
        }

        double totalBytes = targetDrive.TotalSize;
        double freeBytes = targetDrive.AvailableFreeSpace;
        double usedBytes = totalBytes - freeBytes;

        const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;

        snapshot.DiskTotalGb = totalBytes / bytesPerGb;
        snapshot.DiskUsedGb = usedBytes / bytesPerGb;
    }

    private async Task CollectWindowsMetricsAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        CollectWindowsMemory(snapshot);
        await CollectWindowsCpuAsync(snapshot, cancellationToken);
        CollectWindowsUptime(snapshot);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static void CollectWindowsMemory(MetricSnapshot snapshot)
    {
        var memStatus = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        if (!GlobalMemoryStatusEx(ref memStatus))
        {
            return;
        }

        const double bytesPerMb = 1024.0 * 1024.0;

        double totalMb = memStatus.ullTotalPhys / bytesPerMb;
        double availMb = memStatus.ullAvailPhys / bytesPerMb;

        snapshot.MemoryTotalMb = totalMb;
        snapshot.MemoryUsedMb = totalMb - availMb;
    }

    private static async Task CollectWindowsCpuAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        var startCpu = GetTotalProcessorTime();
        var startTime = DateTime.UtcNow;

        await Task.Delay(1000, cancellationToken);

        var endCpu = GetTotalProcessorTime();
        var endTime = DateTime.UtcNow;

        var cpuUsedMs = (endCpu - startCpu).TotalMilliseconds;
        var elapsedMs = (endTime - startTime).TotalMilliseconds;

        var cpuCount = Environment.ProcessorCount;

        var usage = cpuUsedMs / (elapsedMs * cpuCount) * 100.0;

        snapshot.CpuUsagePercent = Math.Round(Math.Clamp(usage, 0, 100), 2);
    }

    private static TimeSpan GetTotalProcessorTime()
    {
        var total = TimeSpan.Zero;
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                total += process.TotalProcessorTime;
            }
            catch
            {

            }
        }
        return total;
    }

    private static void CollectWindowsUptime(MetricSnapshot snapshot)
    {
        snapshot.UptimeSeconds = Environment.TickCount64 / 1000.0;
    }
}