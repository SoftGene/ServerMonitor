using System.Runtime.InteropServices;
using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Monitoring;

public class MetricsCollector : IMetricsCollector
{
    /// <summary>
    /// Интервал между двумя замерами счётчиков процессора. Загрузку нельзя измерить
    /// мгновенно: ядро хранит накопленные счётчики, а загрузка — разница между двумя
    /// моментами, делённая на прошедшее время.
    /// </summary>
    private static readonly TimeSpan CpuSampleInterval = TimeSpan.FromSeconds(1);

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

    // ===== Linux =====

    private async Task CollectLinuxMetricsAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        await CollectLinuxMemoryAsync(snapshot, cancellationToken);
        await CollectLinuxUptimeAsync(snapshot, cancellationToken);
        await CollectLinuxCpuAsync(snapshot, cancellationToken);
    }

    private async Task CollectLinuxMemoryAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken);

        var (totalKb, availableKb) = ProcParser.ParseMemInfo(lines);

        // MemAvailable, а не MemFree: свободную память ядро отдаёт под дисковый кэш,
        // поэтому MemFree на живой системе всегда близок к нулю.
        snapshot.MemoryTotalMb = totalKb / 1024.0;
        snapshot.MemoryUsedMb = (totalKb - availableKb) / 1024.0;
    }

    private async Task CollectLinuxUptimeAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync("/proc/uptime", cancellationToken);

        snapshot.UptimeSeconds = ProcParser.ParseUptimeSeconds(content);
    }

    private async Task CollectLinuxCpuAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        var first = await ReadCpuTimesAsync(cancellationToken);

        await Task.Delay(CpuSampleInterval, cancellationToken);

        var second = await ReadCpuTimesAsync(cancellationToken);

        snapshot.CpuUsagePercent = ProcParser.CalculateCpuUsagePercent(first, second);
    }

    private async Task<CpuTimes> ReadCpuTimesAsync(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync("/proc/stat", cancellationToken);

        if (lines.Length == 0)
        {
            throw new FormatException("/proc/stat is empty.");
        }

        return ProcParser.ParseCpuTimes(lines[0]);
    }

    // ===== Windows =====

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

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Системный аналог /proc/stat: суммарное время простоя, время ядра и время
    /// пользовательских программ с момента загрузки. Время ядра уже включает простой.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    private static void CollectWindowsMemory(MetricSnapshot snapshot)
    {
        var memStatus = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        if (!GlobalMemoryStatusEx(ref memStatus))
        {
            throw new InvalidOperationException(
                $"GlobalMemoryStatusEx failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        const double bytesPerMb = 1024.0 * 1024.0;

        double totalMb = memStatus.ullTotalPhys / bytesPerMb;
        double availMb = memStatus.ullAvailPhys / bytesPerMb;

        snapshot.MemoryTotalMb = totalMb;
        snapshot.MemoryUsedMb = totalMb - availMb;
    }

    private static async Task CollectWindowsCpuAsync(MetricSnapshot snapshot, CancellationToken cancellationToken)
    {
        var first = ReadWindowsCpuTimes();

        await Task.Delay(CpuSampleInterval, cancellationToken);

        var second = ReadWindowsCpuTimes();

        snapshot.CpuUsagePercent = ProcParser.CalculateCpuUsagePercent(first, second);
    }

    private static CpuTimes ReadWindowsCpuTimes()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            throw new InvalidOperationException(
                $"GetSystemTimes failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        // kernelTime уже содержит время простоя, поэтому общее время — это kernel + user.
        var idle = ToTicks(idleTime);
        var total = ToTicks(kernelTime) + ToTicks(userTime);

        return new CpuTimes { Idle = idle, Total = total };
    }

    /// <summary>Склеивает две 32-битные половины FILETIME в одно 64-битное значение.</summary>
    private static long ToTicks(FILETIME fileTime)
    {
        return (long)(((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime);
    }

    private static void CollectWindowsUptime(MetricSnapshot snapshot)
    {
        snapshot.UptimeSeconds = Environment.TickCount64 / 1000.0;
    }

    // ===== Диск (одинаково для обеих систем) =====

    private static void CollectDiskMetrics(MetricSnapshot snapshot)
    {
        var targetDrive = FindSystemDrive();

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

    /// <summary>
    /// Ищет раздел, на котором стоит система. Раньше здесь брался «первый готовый» диск —
    /// на машине с несколькими томами это была лотерея.
    /// </summary>
    private static DriveInfo? FindSystemDrive()
    {
        var readyDrives = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .ToList();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return readyDrives.FirstOrDefault(drive => drive.Name == "/");
        }

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);

        if (!string.IsNullOrEmpty(systemRoot))
        {
            var systemDrive = readyDrives.FirstOrDefault(drive =>
                string.Equals(drive.Name, systemRoot, StringComparison.OrdinalIgnoreCase));

            if (systemDrive is not null)
            {
                return systemDrive;
            }
        }

        return readyDrives.FirstOrDefault();
    }
}
