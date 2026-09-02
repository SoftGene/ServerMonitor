namespace ServerMonitor.Infrastructure.Monitoring;

/// <summary>
/// Настройки мониторинга из секции "Monitoring" конфигурации. Раньше эти числа были
/// константами в полях сервисов, и поменять частоту сбора можно было только перекомпиляцией.
/// </summary>
public class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    /// <summary>
    /// Пауза между снятием метрик. Реальный шаг больше на время самого замера: внутри
    /// сбора есть секундная пауза для вычисления загрузки процессора.
    /// </summary>
    public int CollectIntervalSeconds { get; set; } = 5;

    /// <summary>Пауза между проверками порогов.</summary>
    public int AlertCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Сколько проверок подряд значение должно превышать порог, прежде чем отправить
    /// оповещение. Защита от дребезга: одиночный всплеск загрузки больше не поднимает тревогу.
    /// </summary>
    public int AlertConsecutiveSamples { get; set; } = 3;

    public TimeSpan CollectInterval => TimeSpan.FromSeconds(Math.Max(1, CollectIntervalSeconds));

    public TimeSpan AlertCheckInterval => TimeSpan.FromSeconds(Math.Max(1, AlertCheckIntervalSeconds));

    public int RequiredConsecutiveSamples => Math.Max(1, AlertConsecutiveSamples);
}
