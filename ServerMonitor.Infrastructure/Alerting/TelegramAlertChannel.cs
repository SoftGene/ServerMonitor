using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServerMonitor.Domain.Entities;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>
/// Отправляет уведомление в настроенный чат Telegram. Если бот не настроен, канал молча
/// бездействует — это его нормальное состояние, а не ошибка.
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
            // Недоставленное сообщение не должно ронять проверку правил: событие уже
            // сохранено в журнале, а остальные каналы должны получить свой шанс.
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

        return n.Kind == AlertKind.Triggered
            ? $"⚠️ <b>{metric} Alert</b> · {name}\n" +
              $"{metric} usage is high: <b>{n.Value:F1}%</b> (threshold {n.Threshold:F0}%)"
            : $"✅ <b>{metric} Recovered</b> · {name}\n" +
              $"{metric} usage back to normal: <b>{n.Value:F1}%</b>";
    }

    /// <summary>
    /// Экранирует имя машины: оно приходит от агента, то есть снаружи, а уходит в сообщение
    /// с parseMode=Html, где символ &lt; сломал бы разметку.
    /// </summary>
    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
