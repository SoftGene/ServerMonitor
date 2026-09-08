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
        var state = notification.Kind == AlertKind.Triggered ? "TRIGGERED" : "RECOVERED";

        if (notification.Metric == MetricKind.Availability)
        {
            // The two wordings differ for a reason: "unreachable for 2 min" at the moment of
            // recovery reads as "still unreachable" and misleads.
            if (notification.Kind == AlertKind.Triggered)
            {
                _logger.LogWarning(
                    "Alert {State}: {Server} unreachable for {Minutes:F0} min (threshold {Threshold:F0} min).",
                    state,
                    notification.ServerName,
                    notification.Value,
                    notification.Threshold);
            }
            else
            {
                _logger.LogWarning(
                    "Alert {State}: {Server} is reporting again after {Minutes:F0} min of silence.",
                    state,
                    notification.ServerName,
                    notification.Value);
            }

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
