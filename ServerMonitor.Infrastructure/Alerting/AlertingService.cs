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
/// Checks the rules and hands events to the delivery channels.
/// </summary>
/// <remarks>
/// This work used to live inside TelegramBotService, whose early return when no bot was
/// configured meant thresholds were not checked at all: the delivery channel was the on/off
/// switch for the whole feature. Here the checking depends on no channel — events reach the
/// alert journal either way.
/// </remarks>
public class AlertingService : BackgroundService
{
    private static readonly MetricKind[] ThresholdKinds =
        [MetricKind.Cpu, MetricKind.Memory, MetricKind.Disk];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnumerable<IAlertChannel> _channels;
    private readonly MonitoringOptions _options;
    private readonly ILogger<AlertingService> _logger;

    /// <summary>State per machine-and-metric pair, availability included.</summary>
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

        // After the API has been down, every machine looks missing even though each will
        // report within seconds. The first pass skips the availability check, giving the
        // agents a window to announce themselves.
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
    /// Restores open alerts from the journal, so a restart neither repeats what has already
    /// been announced nor loses a recovery that is still owed.
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
                        // The event's Value records how many minutes the machine had been
                        // silent when it fired. Subtracting that from its timestamp gives
                        // back the moment of the last reading before it went quiet.
                        state.SilentSinceUtc = lastAlert.TimestampUtc.AddMinutes(-lastAlert.Value);
                    }
                }
            }

            _logger.LogInformation(
                "Alert state restored for {Count} server(s) from the database.", serverIds.Count);
        }
        catch (Exception ex)
        {
            // Could not read it — start from a clean state. Worse than an exact restore,
            // but no reason to leave alerting switched off entirely.
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
                // The machine is silent, so its newest reading is stale and says nothing
                // about current load. Its disappearance was already reported above.
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

        // Downtime is measured from the last reading before the silence. If that state could
        // not be restored, zero is more honest than an invented number.
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

            // A single spike raises nothing: several consecutive breaches are required.
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
    /// Writes the event to the journal and hands it to the channels. The write comes first:
    /// the journal is the source of truth that state is restored from, and it must not depend
    /// on whether a message was delivered.
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

    /// <summary>Drops state for machines that no longer exist, so the dictionary stops growing.</summary>
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
