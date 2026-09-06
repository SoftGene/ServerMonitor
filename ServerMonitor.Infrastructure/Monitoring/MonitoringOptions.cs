namespace ServerMonitor.Infrastructure.Monitoring;

/// <summary>
/// Настройки алертинга из секции "Monitoring" конфигурации API.
/// <para>
/// Интервал сбора здесь больше не живёт: сбором занимается агент на наблюдаемой машине,
/// и частота замеров — его настройка (<c>Agent:CollectIntervalSeconds</c>). Держать её
/// в конфигурации сервера было бы враньём — сервер на неё никак не влияет.
/// </para>
/// </summary>
public class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    /// <summary>Пауза между проверками порогов.</summary>
    public int AlertCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Сколько проверок подряд значение должно превышать порог, прежде чем отправить
    /// оповещение. Защита от дребезга: одиночный всплеск не поднимает тревогу.
    /// </summary>
    public int AlertConsecutiveSamples { get; set; } = 3;

    public TimeSpan AlertCheckInterval => TimeSpan.FromSeconds(Math.Max(1, AlertCheckIntervalSeconds));

    public int RequiredConsecutiveSamples => Math.Max(1, AlertConsecutiveSamples);
}
