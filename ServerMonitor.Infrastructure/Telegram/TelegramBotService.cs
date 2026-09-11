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
/// Answers commands in Telegram. Nothing else.
/// </summary>
/// <remarks>
/// This class used to do three jobs at once: hold the long polling connection, check the
/// thresholds and send the messages. Because of that, its early return when no bot was
/// configured switched off the rule checking too. Checking moved to <c>AlertingService</c>
/// and sending to <c>TelegramAlertChannel</c>.
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

        if (string.IsNullOrWhiteSpace(token))
        {
            // Only command handling stops here. Rule checking lives elsewhere and keeps running.
            _logger.LogWarning("Telegram bot token is not configured. Bot commands disabled.");
            return;
        }

        _botClient = new TelegramBotClient(token);

        if (string.IsNullOrWhiteSpace(_chatId))
        {
            // Setup mode. With a token and no chat ID the bot used not to start at all, leaving a new
            // owner no way to learn their chat ID from inside Telegram. Now it starts, answers any
            // message with the sender's own ID, and serves nothing else until the ID is configured.
            _logger.LogWarning(
                "Telegram bot is in setup mode: no chat ID is configured. Send the bot any message "
                + "and it replies with the chat ID to put into TELEGRAM_CHAT_ID.");
        }
        else
        {
            try
            {
                await _botClient.SendMessage(
                    _chatId, "🟢 Server Monitor started. Alerts are active.", cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Telegram startup message.");
            }
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

        // Receiving updates runs in the background; this method just waits for shutdown.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // An orderly shutdown.
        }

        _logger.LogInformation("Telegram bot stopped.");
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { Text: { } messageText } message)
            return;

        switch (TelegramAccess.Classify(_chatId, message.Chat.Id))
        {
            case TelegramAccess.Decision.Ignore:
                // Reply only into the configured chat: otherwise anyone who finds the bot could ask
                // /status and receive the server's metrics.
                _logger.LogWarning("Ignored command from unauthorized chat {ChatId}.", message.Chat.Id);
                return;

            case TelegramAccess.Decision.SetupHint:
                _logger.LogInformation("Setup mode: told chat {ChatId} its ID.", message.Chat.Id);

                try
                {
                    await bot.SendMessage(
                        message.Chat.Id,
                        TelegramAccess.SetupHint(message.Chat.Id),
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send the setup hint.");
                }

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

        var servers = await dbContext.Servers
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.LastSeenUtc, s.OfflineAfterSeconds })
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

            // The machine's own silence threshold when it has one, as on the dashboard. Reading the
            // fleet default here called a laptop allowed to sleep offline in Telegram while the
            // dashboard, correctly, called it stale.
            var offlineAfter = ServerHealthCalculator.ResolveOfflineAfter(
                server.OfflineAfterSeconds, settings?.OfflineAfterSeconds);

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
    /// Escapes the machine name for parseMode=Html. The name arrives from an agent, which is
    /// to say from outside, and a &lt; would break the message markup.
    /// </summary>
    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
