namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>Текущее состояние тревоги по паре «машина + метрика».</summary>
public sealed class MetricAlertState
{
    /// <summary>Тревога открыта: сообщение уже отправлено, повторять не нужно.</summary>
    public bool IsAlerting { get; set; }

    /// <summary>Сколько проверок подряд значение держится выше порога.</summary>
    public int ConsecutiveHighSamples { get; set; }

    /// <summary>
    /// Для событий доступности — момент последнего замера перед тем, как машина замолчала.
    /// Нужен, чтобы в сообщении о возвращении назвать длительность простоя. После
    /// перезапуска восстанавливается из журнала: у события Triggered записано, сколько
    /// минут машина молчала на момент срабатывания, — отняв их от времени события,
    /// получаем ту же точку.
    /// </summary>
    public DateTime? SilentSinceUtc { get; set; }
}
