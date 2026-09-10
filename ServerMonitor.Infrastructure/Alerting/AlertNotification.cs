using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>
/// What happened, in a form that does not depend on how it will be delivered. No ready-made
/// text is handed to a channel: the wording depends on the channel — Telegram wants markup, a
/// log line does not — while the fact is the same for all of them.
/// </summary>
/// <param name="ServerName">The machine the event happened on.</param>
/// <param name="Metric">The metric; <see cref="MetricKind.Availability"/> means an availability event.</param>
/// <param name="Kind">Whether it started or ended.</param>
/// <param name="Value">For thresholds, the metric in percent; for availability, minutes of silence.</param>
/// <param name="Threshold">The threshold that was crossed, in the same units as <paramref name="Value"/>.</param>
/// <param name="Severity">
/// For a start, the level entered; for an end, the level that closed. Each channel decides how
/// loudly to say it — which is the point of passing the fact rather than the sentence.
/// </param>
public record AlertNotification(
    string ServerName,
    MetricKind Metric,
    AlertKind Kind,
    double Value,
    double Threshold,
    AlertSeverity Severity);
