using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>
/// Что произошло — в виде, не зависящем от канала доставки. Каналу не передаётся готовый
/// текст: формулировка зависит от канала (в Telegram уместна разметка, в журнале нет),
/// а вот факт один и тот же.
/// </summary>
/// <param name="ServerName">Машина, на которой произошло событие.</param>
/// <param name="Metric">Метрика; <see cref="MetricKind.Availability"/> — событие доступности.</param>
/// <param name="Kind">Началось или закончилось.</param>
/// <param name="Value">Для порогов — значение метрики в процентах, для доступности — минут молчания.</param>
/// <param name="Threshold">Порог в тех же единицах, что и <paramref name="Value"/>.</param>
public record AlertNotification(
    string ServerName,
    MetricKind Metric,
    AlertKind Kind,
    double Value,
    double Threshold);
