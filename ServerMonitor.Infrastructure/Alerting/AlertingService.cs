using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;
using ServerMonitor.Infrastructure.Monitoring;

namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>
/// Проверяет правила и раздаёт события по каналам доставки.
/// </summary>
/// <remarks>
/// Раньше эта работа жила внутри TelegramBotService, и его ранний выход при ненастроенном
/// боте означал, что пороги не проверяются вовсе: канал доставки был выключателем всей
/// функции. Здесь проверка не зависит ни от одного канала — события в любом случае
/// попадают в журнал алертов.
/// </remarks>
public class AlertingService : BackgroundService
{
    private static readonly MetricKind[] ThresholdKinds =
        [MetricKind.Cpu, MetricKind.Memory, MetricKind.Disk];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnumerable<IAlertChannel> _channels;
    private readonly MonitoringOptions _options;
    private readonly ILogger<AlertingService> _logger;

    /// <summary>Состояние по паре «машина + метрика», включая доступность.</summary>
    private readonly Dictionary<(int ServerId, MetricKind Kind), MetricAlertState> _states = new();

    public AlertingService(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IAlertChannel> channels,
        IOptions<MonitoringOptions> options,
        ILogger<AlertingService> logger)
    {
        _scopeFactory = scopeFactory;
        _channels = channels;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Alerting service started.");

        await RestoreStateAsync(stoppingToken);

        // После простоя API все машины выглядят пропавшими, хотя отчитаются в ближайшие
        // секунды. Первый проход пропускает проверку доступности, давая агентам объявиться.
        var isFirstPass = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(checkHeartbeat: !isFirstPass, stoppingToken);
                isFirstPass = false;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during the alerting check.");
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

        _logger.LogInformation("Alerting service stopped.");
    }

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
    /// Восстанавливает открытые тревоги по журналу, чтобы перезапуск не приводил к повторным
    /// сообщениям о том, о чём уже сообщили, и не терял ожидаемое «восстановление».
    /// </summary>
    private async Task RestoreStateAsync(CancellationToken cancellationToken)
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
                foreach (var kind in Enum.GetValues<MetricKind>())
                {
                    var lastAlert = await dbContext.Alerts
                        .AsNoTracking()
                        .Where(a => a.ServerId == serverId && a.MetricType == kind)
                        .OrderByDescending(a => a.TimestampUtc)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (lastAlert is null || lastAlert.AlertType != AlertKind.Triggered)
                    {
                        continue;
                    }

                    var state = StateFor(serverId, kind);
                    state.IsAlerting = true;

                    if (kind == MetricKind.Availability)
                    {
                        // В Value события записано, сколько минут машина молчала на момент
                        // срабатывания. Отняв их от времени события, получаем момент
                        // последнего замера перед пропажей.
                        state.SilentSinceUtc = lastAlert.TimestampUtc.AddMinutes(-lastAlert.Value);
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

    private async Task CheckAsync(bool checkHeartbeat, CancellationToken cancellationToken)
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
            .Select(s => new { s.Id, s.Name, s.LastSeenUtc })
            .ToListAsync(cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var offlineAfter = TimeSpan.FromSeconds(Math.Max(1, settings.OfflineAfterSeconds));

        foreach (var server in servers)
        {
            if (checkHeartbeat && settings.HeartbeatAlertsEnabled)
            {
                await CheckHeartbeatAsync(
                    dbContext, server.Id, server.Name, server.LastSeenUtc,
                    nowUtc, offlineAfter, cancellationToken);
            }

            var health = ServerHealthCalculator.FromLastSeen(
                server.LastSeenUtc, nowUtc, offlineAfter: offlineAfter);

            if (health == ServerHealth.Offline)
            {
                // Машина молчит — её последний замер устарел, судить о нагрузке по нему
                // бессмысленно. О самой пропаже уже сообщено выше.
                continue;
            }

            var latest = await dbContext.MetricSnapshots
                .AsNoTracking()
                .Where(m => m.ServerId == server.Id)
                .OrderByDescending(m => m.TimestampUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest is null)
            {
                continue;
            }

            await CheckThresholdAsync(dbContext, server.Id, server.Name, MetricKind.Cpu,
                latest.CpuUsagePercent, settings.CpuThreshold, cancellationToken);

            await CheckThresholdAsync(dbContext, server.Id, server.Name, MetricKind.Memory,
                latest.MemoryUsagePercent, settings.MemoryThreshold, cancellationToken);

            await CheckThresholdAsync(dbContext, server.Id, server.Name, MetricKind.Disk,
                latest.DiskUsagePercent, settings.DiskThreshold, cancellationToken);
        }

        PruneRemovedServers(servers.Select(s => s.Id).ToHashSet());
    }

    private async Task CheckHeartbeatAsync(
        AppDbContext dbContext,
        int serverId,
        string serverName,
        DateTime? lastSeenUtc,
        DateTime nowUtc,
        TimeSpan offlineAfter,
        CancellationToken cancellationToken)
    {
        var state = StateFor(serverId, MetricKind.Availability);
        var change = HeartbeatRule.Evaluate(state.IsAlerting, lastSeenUtc, nowUtc, offlineAfter);

        if (change is null)
        {
            return;
        }

        var thresholdMinutes = offlineAfter.TotalMinutes;

        if (change == AlertKind.Triggered)
        {
            state.IsAlerting = true;
            state.SilentSinceUtc = lastSeenUtc;

            var silentMinutes = (nowUtc - lastSeenUtc!.Value).TotalMinutes;

            await RaiseAsync(
                dbContext, serverId, serverName, MetricKind.Availability, AlertKind.Triggered,
                silentMinutes, thresholdMinutes, cancellationToken);

            return;
        }

        state.IsAlerting = false;

        // Длительность простоя считаем от последнего замера перед пропажей. Если состояние
        // восстановить не удалось, честнее показать ноль, чем выдумать число.
        var downtimeMinutes = state.SilentSinceUtc is null
            ? 0
            : (nowUtc - state.SilentSinceUtc.Value).TotalMinutes;

        state.SilentSinceUtc = null;

        await RaiseAsync(
            dbContext, serverId, serverName, MetricKind.Availability, AlertKind.Recovered,
            downtimeMinutes, thresholdMinutes, cancellationToken);
    }

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

        if (value > threshold)
        {
            state.ConsecutiveHighSamples++;

            // Одиночный всплеск не поднимает тревогу: нужно несколько превышений подряд.
            if (!state.IsAlerting && state.ConsecutiveHighSamples >= _options.RequiredConsecutiveSamples)
            {
                state.IsAlerting = true;

                await RaiseAsync(dbContext, serverId, serverName, kind, AlertKind.Triggered,
                    Math.Round(value, 1), threshold, cancellationToken);
            }

            return;
        }

        state.ConsecutiveHighSamples = 0;

        if (state.IsAlerting)
        {
            state.IsAlerting = false;

            await RaiseAsync(dbContext, serverId, serverName, kind, AlertKind.Recovered,
                Math.Round(value, 1), threshold, cancellationToken);
        }
    }

    /// <summary>
    /// Записывает событие в журнал и раздаёт его каналам. Сначала запись: журнал — источник
    /// правды, по которому восстанавливается состояние, и он не должен зависеть от того,
    /// дошло ли сообщение.
    /// </summary>
    private async Task RaiseAsync(
        AppDbContext dbContext,
        int serverId,
        string serverName,
        MetricKind kind,
        AlertKind alertKind,
        double value,
        double threshold,
        CancellationToken cancellationToken)
    {
        dbContext.Alerts.Add(new Alert
        {
            ServerId = serverId,
            TimestampUtc = DateTime.UtcNow,
            MetricType = kind,
            Value = Math.Round(value, 1),
            Threshold = threshold,
            AlertType = alertKind
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var notification = new AlertNotification(serverName, kind, alertKind, value, threshold);

        foreach (var channel in _channels)
        {
            await channel.SendAsync(notification, cancellationToken);
        }
    }

    /// <summary>Убирает состояние машин, которых больше нет, чтобы словарь не рос вечно.</summary>
    private void PruneRemovedServers(HashSet<int> existingServerIds)
    {
        var stale = _states.Keys
            .Where(key => !existingServerIds.Contains(key.ServerId))
            .ToList();

        foreach (var key in stale)
        {
            _states.Remove(key);
        }
    }
}
