using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;
using ServerMonitor.Infrastructure.Monitoring;
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
    private readonly MonitoringOptions _options;

    private ITelegramBotClient? _botClient;
    private string? _chatId;

    /// <summary>
    /// Состояние по каждой метрике. Раньше это были три отдельных поля, которые
    /// передавались в проверку парой делегатов «прочитать» и «записать».
    /// </summary>
    private readonly Dictionary<MetricKind, MetricAlertState> _states = new()
    {
        [MetricKind.Cpu] = new MetricAlertState(),
        [MetricKind.Memory] = new MetricAlertState(),
        [MetricKind.Disk] = new MetricAlertState()
    };

    public TelegramBotService(
        IConfiguration configuration,
        ILogger<TelegramBotService> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<MonitoringOptions> options)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options.Value;
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

        // Состояние алертов живёт в базе: после перезапуска мы не должны заново
        // сообщать о том, о чём уже сообщали, и не должны терять «восстановление».
        await RestoreAlertStateAsync(stoppingToken);

        try
        {
            await _botClient.SendMessage(_chatId, "🟢 Server Monitor started. Alerts are active.", cancellationToken: stoppingToken);
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

            try
            {
                await Task.Delay(_options.AlertCheckInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Восстанавливает состояние по последней записи в журнале алертов для каждой метрики.
    /// Если последним событием было «Triggered», значит тревога всё ещё открыта.
    /// </summary>
    private async Task RestoreAlertStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            foreach (var kind in _states.Keys)
            {
                var lastAlert = await dbContext.Alerts
                    .AsNoTracking()
                    .Where(a => a.MetricType == kind)
                    .OrderByDescending(a => a.TimestampUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (lastAlert is not null)
                {
                    _states[kind].IsAlerting = lastAlert.AlertType == AlertKind.Triggered;
                }
            }

            _logger.LogInformation("Alert state restored from the database.");
        }
        catch (Exception ex)
        {
            // Не смогли прочитать — стартуем с чистого состояния. Хуже, чем точное
            // восстановление, но не повод не запускать оповещения вовсе.
            _logger.LogError(ex, "Failed to restore alert state; starting with a clean state.");
        }
    }

    private async Task CheckMetricsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await dbContext.AppSettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null || !settings.AlertsEnabled)
        {
            return;
        }

        var latest = await dbContext.MetricSnapshots
            .AsNoTracking()
            .OrderByDescending(m => m.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return;
        }

        await CheckThresholdAsync(dbContext, MetricKind.Cpu, latest.CpuUsagePercent, settings.CpuThreshold, cancellationToken);
        await CheckThresholdAsync(dbContext, MetricKind.Memory, latest.MemoryUsagePercent, settings.MemoryThreshold, cancellationToken);
        await CheckThresholdAsync(dbContext, MetricKind.Disk, latest.DiskUsagePercent, settings.DiskThreshold, cancellationToken);
    }

    /// <summary>
    /// Реагирует на переход состояния, а не на само состояние: сообщение уходит один раз
    /// при срабатывании и один раз при возврате в норму.
    /// </summary>
    private async Task CheckThresholdAsync(
        AppDbContext dbContext,
        MetricKind kind,
        double value,
        double threshold,
        CancellationToken cancellationToken)
    {
        var state = _states[kind];
        var name = kind.ToDisplayName();

        if (value > threshold)
        {
            state.ConsecutiveHighSamples++;

            // Одиночный всплеск не поднимает тревогу: нужно несколько превышений подряд.
            if (!state.IsAlerting && state.ConsecutiveHighSamples >= _options.RequiredConsecutiveSamples)
            {
                state.IsAlerting = true;

                await SendAlertAsync(
                    $"⚠️ <b>{name} Alert</b>\n{name} usage is high: <b>{value:F1}%</b> (threshold {threshold}%)",
                    cancellationToken);

                await SaveAlertAsync(dbContext, kind, value, threshold, AlertKind.Triggered, cancellationToken);
            }
        }
        else
        {
            state.ConsecutiveHighSamples = 0;

            if (state.IsAlerting)
            {
                state.IsAlerting = false;

                await SendAlertAsync(
                    $"✅ <b>{name} Recovered</b>\n{name} usage back to normal: <b>{value:F1}%</b>",
                    cancellationToken);

                await SaveAlertAsync(dbContext, kind, value, threshold, AlertKind.Recovered, cancellationToken);
            }
        }
    }

    private async Task SaveAlertAsync(
        AppDbContext dbContext,
        MetricKind metricType,
        double value,
        double threshold,
        AlertKind alertType,
        CancellationToken cancellationToken)
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

    private async Task SendAlertAsync(string message, CancellationToken cancellationToken)
    {
        if (_botClient is null || _chatId is null)
        {
            return;
        }

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

        var latest = await dbContext.MetricSnapshots
            .AsNoTracking()
            .OrderByDescending(m => m.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return "No metrics available yet.";
        }

        return $"📊 <b>Server Status</b>\n\n" +
               $"🖥 CPU: <b>{latest.CpuUsagePercent:F1}%</b>\n" +
               $"💾 Memory: <b>{latest.MemoryUsagePercent:F1}%</b>\n" +
               $"💿 Disk: <b>{latest.DiskUsagePercent:F1}%</b>\n\n" +
               $"🕐 Updated: {latest.TimestampUtc.ToLocalTime():HH:mm:ss}";
    }

    /// <summary>Текущее состояние тревоги по одной метрике.</summary>
    private sealed class MetricAlertState
    {
        /// <summary>Тревога открыта: сообщение уже отправлено, повторять не нужно.</summary>
        public bool IsAlerting { get; set; }

        /// <summary>Сколько проверок подряд значение держится выше порога.</summary>
        public int ConsecutiveHighSamples { get; set; }
    }
}
