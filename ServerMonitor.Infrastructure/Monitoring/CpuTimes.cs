namespace ServerMonitor.Infrastructure.Monitoring;

/// <summary>
/// Накопленные счётчики времени процессора на момент замера.
/// Загрузка вычисляется как разница между двумя такими замерами.
/// </summary>
public readonly struct CpuTimes
{
    /// <summary>Время простоя (в тактах или интервалах, зависит от источника).</summary>
    public long Idle { get; init; }

    /// <summary>Суммарное время во всех состояниях, включая простой.</summary>
    public long Total { get; init; }
}
