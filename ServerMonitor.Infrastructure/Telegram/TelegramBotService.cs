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

    private static readonly MetricKind[] TrackedKinds =
        [MetricKind.Cpu, MetricKind.Memory, MetricKind.Disk];

    /// <summary>
    /// Состояние тревоги по паре «машина + метрика». Ключом раньше была одна метрика,
    /// потому что машина подразумевалась единственной: с парком такое состояние
    /// перемигивалось бы между серверами — превышение на одном гасило бы тревогу другого.
    /// </summary>
    private readonly Dictionary<(int ServerId, MetricKind Kind), MetricAlertState> _states = new();

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

    /// <summary>Состояние тревоги для пары «машина + метрика», заводится при первом обращении.</summary>
    private MetricAlertState StateFor(int serverId, MetricKind kind)
    {
        var key = (serverId, kind);

        if (!_states.TryGetValue(key, out var state))
        {
            state = new MetricAlertState();
            _states[key] = state;
        }

        return state;
    }

    /// <summary>
    /// Восстанавливает состояние по последней записи в журнале для каждой пары
    /// «машина + метрика». Если последним событием было «Triggered», тревога всё ещё открыта.
    /// </summary>
    private async Task RestoreAlertStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var serverIds = await dbContext.Servers
                .AsNoTracking()
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            foreach (var serverId in serverIds)
            {
                foreach (var kind in TrackedKinds)
                {
                    var lastAlert = await dbContext.Alerts
                        .AsNoTracking()
                        .Where(a => a.ServerId == serverId && a.MetricType == kind)
                        .OrderByDescending(a => a.TimestampUtc)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (lastAlert is not null)
                    {
                        StateFor(serverId, kind).IsAlerting = lastAlert.AlertType == AlertKind.Triggered;
                    }
                }
            }

            _logger.LogInformation(
                "Alert state restored for {Count} server(s) from the database.", serverIds.Count);
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

        var servers = await dbContext.Servers
            .AsNoTracking()
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(cancellationToken);

        var nowUtc = DateTime.UtcNow;

        foreach (var server in servers)
        {
            var latest = await dbContext.MetricSnapshots
                .AsNoTracking()
                .Where(m => m.ServerId == server.Id)
                .OrderByDescending(m => m.TimestampUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest is null)
            {
                continue;
            }

            // Машина молчит — её последний замер устарел, судить о нагрузке по нему нельзя.
            // Оповещение о самой пропаже машины — задача отдельного этапа (heartbeat).
            if (ServerHealthCalculator.FromLastSeen(latest.TimestampUtc, nowUtc) == ServerHealth.Offline)
            {
                continue;
            }

            await CheckThresholdAsync(
                dbContext, server.Id, server.Name, MetricKind.Cpu,
                latest.CpuUsagePercent, settings.CpuThreshold, cancellationToken);

            await CheckThresholdAsync(
                dbContext, server.Id, server.Name, MetricKind.Memory,
                latest.MemoryUsagePercent, settings.MemoryThreshold, cancellationToken);

            await CheckThresholdAsync(
                dbContext, server.Id, server.Name, MetricKind.Disk,
                latest.DiskUsagePercent, settings.DiskThreshold, cancellationToken);
        }
    }

    /// <summary>
    /// Реагирует на переход состояния, а не на само состояние: сообщение уходит один раз
    /// при срабатывании и один раз при возврате в норму.
    /// </summary>
    private async Task CheckThresholdAsync(
        AppDbContext dbContext,
        int serverId,
        string serverName,
        MetricKind kind,
        double value,
        double threshold,
        CancellationToken cancellationToken)
    {
        var state = StateFor(serverId, kind);
        var name = kind.ToDisplayName();

        if (value > threshold)
        {
            state.ConsecutiveHighSamples++;

            // Одиночный всплеск не поднимает тревогу: нужно несколько превышений подряд.
            if (!state.IsAlerting && state.ConsecutiveHighSamples >= _options.RequiredConsecutiveSamples)
            {
                state.IsAlerting = true;

                // Имя машины в тексте обязательно: сообщение без него не говорит, куда идти.
                await SendAlertAsync(
                    $"⚠️ <b>{name} Alert</b> · {Escape(serverName)}\n{name} usage is high: <b>{value:F1}%</b> (threshold {threshold}%)",
                    cancellationToken);

                await SaveAlertAsync(dbContext, serverId, kind, value, threshold, AlertKind.Triggered, cancellationToken);
            }
        }
        else
        {
            state.ConsecutiveHighSamples = 0;

            if (state.IsAlerting)
            {
                state.IsAlerting = false;

                await SendAlertAsync(
                    $"✅ <b>{name} Recovered</b> · {Escape(serverName)}\n{name} usage back to normal: <b>{value:F1}%</b>",
                    cancellationToken);

                await SaveAlertAsync(dbContext, serverId, kind, value, threshold, AlertKind.Recovered, cancellationToken);
            }
        }
    }

    private async Task SaveAlertAsync(
        AppDbContext dbContext,
        int serverId,
        MetricKind metricType,
        double value,
        double threshold,
        AlertKind alertType,
        CancellationToken cancellationToken)
    {
        var alert = new Alert
        {
            // Без ServerId запись не проходит внешний ключ: с появлением сущности Server
            // алерт без машины перестал быть допустимым.
            ServerId = serverId,
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

            var marker = ServerHealthCalculator.FromLastSeen(server.LastSeenUtc, nowUtc) switch
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

    /// <summary>Текущее состояние тревоги по одной метрике.</summary>
    private sealed class MetricAlertState
    {
        /// <summary>Тревога открыта: сообщение уже отправлено, повторять не нужно.</summary>
        public bool IsAlerting { get; set; }

        /// <summary>Сколько проверок подряд значение держится выше порога.</summary>
        public int ConsecutiveHighSamples { get; set; }
    }
}
