using Microsoft.Extensions.Logging;
using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>
/// Writes the notification to the application log. Always present: it guarantees an event is
/// visible somewhere even when no external channel is configured.
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
        // The log level follows the severity, so filtering the log on Error finds every critical
        // event and nothing else. A recovery is good news and is logged as information.
        var level = notification.Kind == AlertKind.Recovered ? LogLevel.Information
            : notification.Severity == AlertSeverity.Critical ? LogLevel.Error
            : LogLevel.Warning;

        if (notification.Metric == MetricKind.Availability)
        {
            // The two wordings differ for a reason: "unreachable for 2 min" at the moment of
            // recovery reads as "still unreachable" and misleads.
            if (notification.Kind == AlertKind.Triggered)
            {
                _logger.Log(
                    level,
                    "Alert {Severity}: {Server} unreachable for {Minutes:F0} min (threshold {Threshold:F0} min).",
                    notification.Severity,
                    notification.ServerName,
                    notification.Value,
                    notification.Threshold);
            }
            else
            {
                _logger.Log(
                    level,
                    "Alert recovered: {Server} is reporting again after {Minutes:F0} min of silence.",
                    notification.ServerName,
                    notification.Value);
            }

            return Task.CompletedTask;
        }

        if (notification.Kind == AlertKind.Recovered)
        {
            _logger.Log(
                level,
                "Alert recovered: {Server} {Metric} back to {Value:F1}% after a {Severity} alert.",
                notification.ServerName,
                notification.Metric.ToDisplayName(),
                notification.Value,
                notification.Severity);

            return Task.CompletedTask;
        }

        _logger.Log(
            level,
            "Alert {Severity}: {Server} {Metric} at {Value:F1}% (threshold {Threshold:F0}%).",
            notification.Severity,
            notification.ServerName,
            notification.Metric.ToDisplayName(),
            notification.Value,
            notification.Threshold);

        return Task.CompletedTask;
    }
}
