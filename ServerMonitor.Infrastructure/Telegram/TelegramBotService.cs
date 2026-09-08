using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ServerMonitor.Infrastructure.Telegram;

/// <summary>
/// Отвечает на команды в Telegram. Больше ничего.
/// </summary>
/// <remarks>
/// Раньше этот класс делал три дела сразу: держал long polling, проверял пороги и отправлял
/// сообщения. Из-за этого ранний выход при ненастроенном боте выключал заодно и проверку
/// правил. Проверка переехала в <c>AlertingService</c>, отправка — в <c>TelegramAlertChannel</c>.
/// </remarks>
public class TelegramBotService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private ITelegramBotClient? _botClient;
    private string? _chatId;

    public TelegramBotService(
        IConfiguration configuration,
        ILogger<TelegramBotService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = _configuration["Telegram:BotToken"];
        _chatId = _configuration["Telegram:ChatId"];

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(_chatId))
        {
            // Выходим только из обработки команд. Проверка правил живёт отдельно и
            // продолжает работать.
            _logger.LogWarning("Telegram bot token or chat ID is not configured. Bot commands disabled.");
            return;
        }

        _botClient = new TelegramBotClient(token);

        try
        {
            await _botClient.SendMessage(
                _chatId, "🟢 Server Monitor started. Alerts are active.", cancellationToken: stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram startup message.");
        }

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message }
        };

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Telegram bot started receiving commands.");

        // Приём обновлений работает в фоне, этому методу остаётся дождаться остановки.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // Штатная остановка.
        }

        _logger.LogInformation("Telegram bot stopped.");
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { Text: { } messageText } message)
            return;

        // Отвечаем только в настроенный чат: иначе любой, кто найдёт бота,
        // получит метрики сервера по команде /status.
        if (!string.Equals(message.Chat.Id.ToString(), _chatId, StringComparison.Ordinal))
        {
            _logger.LogWarning("Ignored command from unauthorized chat {ChatId}.", message.Chat.Id);
            return;
        }

        var command = messageText.Split(' ')[0].ToLower();

        var response = command switch
        {
            "/start" => "👋 Welcome to Server Monitor!\n\nCommands:\n/status - current metrics\n/help - show commands",
            "/help" => "📋 <b>Commands</b>\n/status - current server metrics\n/help - show this help",
            "/status" => await GetStatusMessageAsync(cancellationToken),
            _ => "Unknown command. Type /help for available commands."
        };

        try
        {
            await bot.SendMessage(message.Chat.Id, response, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reply to command.");
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram polling error.");
        return Task.CompletedTask;
    }

    private async Task<string> GetStatusMessageAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await dbContext.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        var offlineAfter = settings is null
            ? ServerHealthCalculator.OfflineAfter
            : TimeSpan.FromSeconds(Math.Max(1, settings.OfflineAfterSeconds));

        var servers = await dbContext.Servers
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.LastSeenUtc })
            .ToListAsync(cancellationToken);

        if (servers.Count == 0)
        {
            return "No servers registered yet.";
        }

        var nowUtc = DateTime.UtcNow;
        var lines = new List<string> { "📊 <b>Fleet Status</b>" };

        foreach (var server in servers)
        {
            var latest = await dbContext.MetricSnapshots
                .AsNoTracking()
                .Where(m => m.ServerId == server.Id)
                .OrderByDescending(m => m.TimestampUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var marker = ServerHealthCalculator.FromLastSeen(
                server.LastSeenUtc, nowUtc, offlineAfter: offlineAfter) switch
            {
                ServerHealth.Online => "🟢",
                ServerHealth.Stale => "🟡",
                _ => "🔴"
            };

            var name = Escape(server.Name);

            lines.Add(latest is null
                ? $"\n{marker} <b>{name}</b>\nNo readings yet."
                : $"\n{marker} <b>{name}</b>\n" +
                  $"🖥 CPU <b>{latest.CpuUsagePercent:F1}%</b> · " +
                  $"💾 RAM <b>{latest.MemoryUsagePercent:F1}%</b> · " +
                  $"💿 Disk <b>{latest.DiskUsagePercent:F1}%</b>\n" +
                  $"🕐 {latest.TimestampUtc.ToLocalTime():HH:mm:ss}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Экранирует имя машины для parseMode=Html. Имя приходит от агента, то есть
    /// снаружи, а символ &lt; сломал бы разметку сообщения.
    /// </summary>
    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
