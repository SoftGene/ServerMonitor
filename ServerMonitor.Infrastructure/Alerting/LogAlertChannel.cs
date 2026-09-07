using Microsoft.Extensions.Logging;
using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>
/// Пишет уведомление в журнал приложения. Нужен всегда: он гарантирует, что событие
/// где-то видно, даже когда ни один внешний канал не настроен.
/// </summary>
public class LogAlertChannel : IAlertChannel
{
    private readonly ILogger<LogAlertChannel> _logger;

    public LogAlertChannel(ILogger<LogAlertChannel> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(AlertNotification notification, CancellationToken cancellationToken)
    {
        var state = notification.Kind == AlertKind.Triggered ? "TRIGGERED" : "RECOVERED";

        if (notification.Metric == MetricKind.Availability)
        {
            _logger.LogWarning(
                "Alert {State}: {Server} unreachable for {Minutes:F0} min (threshold {Threshold:F0} min).",
                state,
                notification.ServerName,
                notification.Value,
                notification.Threshold);

            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "Alert {State}: {Server} {Metric} at {Value:F1}% (threshold {Threshold:F0}%).",
            state,
            notification.ServerName,
            notification.Metric.ToDisplayName(),
            notification.Value,
            notification.Threshold);

        return Task.CompletedTask;
    }
}
