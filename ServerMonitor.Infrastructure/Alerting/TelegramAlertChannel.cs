using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServerMonitor.Domain.Entities;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>
/// Sends a notification to the configured Telegram chat. With no bot configured the channel
/// quietly does nothing — that is a normal state for it, not an error.
/// </summary>
public class TelegramAlertChannel : IAlertChannel
{
    private readonly ILogger<TelegramAlertChannel> _logger;
    private readonly ITelegramBotClient? _botClient;
    private readonly string? _chatId;

    public TelegramAlertChannel(IConfiguration configuration, ILogger<TelegramAlertChannel> logger)
    {
        _logger = logger;

        var token = configuration["Telegram:BotToken"];
        _chatId = configuration["Telegram:ChatId"];

        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(_chatId))
        {
            _botClient = new TelegramBotClient(token);
        }
        else
        {
            _logger.LogInformation(
                "Telegram alert channel is not configured; alerts will still be stored and logged.");
        }
    }

    public async Task SendAsync(AlertNotification notification, CancellationToken cancellationToken)
    {
        if (_botClient is null || _chatId is null)
        {
            return;
        }

        var text = Format(notification);

        try
        {
            await _botClient.SendMessage(
                _chatId, text, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // An undelivered message must not break the rule checking: the event is already
            // stored in the journal, and the remaining channels deserve their turn.
            _logger.LogError(ex, "Failed to send a Telegram alert.");
        }
    }

    private static string Format(AlertNotification n)
    {
        var name = Escape(n.ServerName);

        if (n.Metric == MetricKind.Availability)
        {
            return n.Kind == AlertKind.Triggered
                ? $"🔌 <b>Machine unreachable</b> · {name}\n" +
                  $"No data for <b>{n.Value:F0} min</b> (threshold {n.Threshold:F0} min)"
                : $"🔄 <b>Machine back online</b> · {name}\n" +
                  $"Reporting again after {n.Value:F0} min of silence";
        }

        var metric = n.Metric.ToDisplayName();

        if (n.Kind == AlertKind.Recovered)
        {
            return $"✅ <b>{metric} back to normal</b> · {name}\n" +
                   $"{metric} usage is down to <b>{n.Value:F1}%</b>";
        }

        // Different symbols, not only different words: on a phone's lock screen the icon is read
        // before the text, and it is what decides whether the message gets opened right now.
        return n.Severity == AlertSeverity.Critical
            ? $"🔴 <b>{metric} critical</b> · {name}\n" +
              $"{metric} usage is at <b>{n.Value:F1}%</b> (critical threshold {n.Threshold:F0}%)"
            : $"🟡 <b>{metric} warning</b> · {name}\n" +
              $"{metric} usage is at <b>{n.Value:F1}%</b> (warning threshold {n.Threshold:F0}%)";
    }

    /// <summary>
    /// Escapes the machine name: it arrives from an agent, which is to say from outside, and
    /// goes into a parseMode=Html message where a &lt; would break the markup.
    /// </summary>
    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
