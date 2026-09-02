using System.Globalization;

namespace ServerMonitor.Infrastructure.Monitoring;

/// <summary>
/// Разбор текстовых форматов Linux /proc и вычисление загрузки процессора.
/// <para>
/// Здесь нет обращений к файловой системе: на вход подаётся уже прочитанный текст.
/// Так эти функции можно покрыть тестами, не имея под рукой Linux.
/// </para>
/// <para>
/// Все числа разбираются с <see cref="CultureInfo.InvariantCulture"/>: содержимое /proc —
/// машинный формат с точкой в роли разделителя дробной части, и от языка системы он не
/// зависит. Без явного указания культуры процесс, запущенный, например, с ru-RU, не смог бы
/// разобрать "348915.42".
/// </para>
/// </summary>
public static class ProcParser
{
    /// <summary>Разбирает содержимое /proc/meminfo. Значения возвращаются в килобайтах.</summary>
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

    /// <summary>Разбирает первое число из /proc/uptime — секунды с момента загрузки системы.</summary>
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
    /// Разбирает суммарную строку "cpu ..." из /proc/stat.
    /// Время ожидания диска (iowait) считается простоем — так же поступает утилита top.
    /// </summary>
    public static CpuTimes ParseCpuTimes(string statLine)
    {
        if (string.IsNullOrWhiteSpace(statLine))
        {
            throw new FormatException("/proc/stat cpu line is empty.");
        }

        var parts = statLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // cpu user nice system idle iowait irq softirq — минимум 8 полей
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
    /// Вычисляет загрузку процессора в процентах по двум замерам счётчиков.
    /// Работает и для Linux (/proc/stat), и для Windows (GetSystemTimes) — счётчики устроены
    /// одинаково: накопленное время простоя и накопленное общее время.
    /// </summary>
    public static double CalculateCpuUsagePercent(CpuTimes first, CpuTimes second)
    {
        var totalDelta = second.Total - first.Total;
        var idleDelta = second.Idle - first.Idle;

        // Счётчики не изменились или пошли назад (перезапуск источника) — считаем нулём,
        // это честнее, чем деление на ноль или отрицательная загрузка.
        if (totalDelta <= 0)
        {
            return 0;
        }

        // Приведение к double обязательно: без него целочисленное деление даст 0,
        // и загрузка всегда получалась бы ровно 100 %.
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
