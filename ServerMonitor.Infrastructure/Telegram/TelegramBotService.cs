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

public class TelegramBotService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private ITelegramBotClient? _botClient;
    private string? _chatId;

    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    // Thresholds (hardcoded for now, will move to Settings later)
    private const double CpuThreshold = 5;
    private const double MemoryThreshold = 90;
    private const double DiskThreshold = 90;

    // Track previous alert state to avoid spam
    private bool _cpuWasHigh = false;
    private bool _memoryWasHigh = false;
    private bool _diskWasHigh = false;

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
            _logger.LogWarning("Telegram bot token or chat ID is not configured. Bot disabled.");
            return;
        }

        _botClient = new TelegramBotClient(token);

        try
        {
            await _botClient.SendMessage(_chatId, "🟢 Server Monitor started. Alerts are active.", cancellationToken: stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram startup message.");
        }

        // Start receiving commands (polling)
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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckMetricsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during metrics check.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckMetricsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var latest = dbContext.MetricSnapshots
            .OrderByDescending(m => m.TimestampUtc)
            .FirstOrDefault();

        if (latest is null) return;

        var cpu = latest.CpuUsagePercent;
        var memory = latest.MemoryTotalMb > 0 ? latest.MemoryUsedMb / latest.MemoryTotalMb * 100 : 0;
        var disk = latest.DiskTotalGb > 0 ? latest.DiskUsedGb / latest.DiskTotalGb * 100 : 0;

        await CheckThreshold(dbContext, cpu, CpuThreshold, "CPU", () => _cpuWasHigh, v => _cpuWasHigh = v, cancellationToken);
        await CheckThreshold(dbContext, memory, MemoryThreshold, "Memory", () => _memoryWasHigh, v => _memoryWasHigh = v, cancellationToken);
        await CheckThreshold(dbContext, disk, DiskThreshold, "Disk", () => _diskWasHigh, v => _diskWasHigh = v, cancellationToken);
    }

    private async Task CheckThreshold(AppDbContext dbContext, double value, double threshold, string name,
        Func<bool> getWasHigh, Action<bool> setWasHigh, CancellationToken cancellationToken)
    {
        bool isHigh = value > threshold;
        bool wasHigh = getWasHigh();

        if (isHigh && !wasHigh)
        {
            await SendAlert($"⚠️ <b>{name} Alert</b>\n{name} usage is high: <b>{value:F1}%</b> (threshold {threshold}%)", cancellationToken);
            await SaveAlert(dbContext, name, value, threshold, "Triggered", cancellationToken);
        }
        else if (!isHigh && wasHigh)
        {
            await SendAlert($"✅ <b>{name} Recovered</b>\n{name} usage back to normal: <b>{value:F1}%</b>", cancellationToken);
            await SaveAlert(dbContext, name, value, threshold, "Recovered", cancellationToken);
        }

        setWasHigh(isHigh);
    }

    private async Task SaveAlert(AppDbContext dbContext, string metricType, double value, double threshold, string alertType, CancellationToken cancellationToken)
    {
        var alert = new Alert
        {
            TimestampUtc = DateTime.UtcNow,
            MetricType = metricType,
            Value = Math.Round(value, 1),
            Threshold = threshold,
            AlertType = alertType
        };

        dbContext.Alerts.Add(alert);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SendAlert(string message, CancellationToken cancellationToken)
    {
        if (_botClient is null || _chatId is null) return;

        try
        {
            await _botClient.SendMessage(_chatId, message, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
            _logger.LogInformation("Alert sent: {Message}", message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alert.");
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { Text: { } messageText } message)
            return;

        var command = messageText.Split(' ')[0].ToLower();

        var response = command switch
        {
            "/start" => "👋 Welcome to Server Monitor!\n\nCommands:\n/status - current metrics\n/help - show commands",
            "/help" => "📋 <b>Commands</b>\n/status - current server metrics\n/help - show this help",
            "/status" => await GetStatusMessage(cancellationToken),
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

    private async Task<string> GetStatusMessage(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var latest = dbContext.MetricSnapshots
            .OrderByDescending(m => m.TimestampUtc)
            .FirstOrDefault();

        if (latest is null)
            return "No metrics available yet.";

        var cpu = latest.CpuUsagePercent;
        var memory = latest.MemoryTotalMb > 0 ? latest.MemoryUsedMb / latest.MemoryTotalMb * 100 : 0;
        var disk = latest.DiskTotalGb > 0 ? latest.DiskUsedGb / latest.DiskTotalGb * 100 : 0;

        return $"📊 <b>Server Status</b>\n\n" +
               $"🖥 CPU: <b>{cpu:F1}%</b>\n" +
               $"💾 Memory: <b>{memory:F1}%</b>\n" +
               $"💿 Disk: <b>{disk:F1}%</b>\n\n" +
               $"🕐 Updated: {latest.TimestampUtc.ToLocalTime():HH:mm:ss}";
    }
}