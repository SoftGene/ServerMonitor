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

                    if (kind == MetricKind.Availability)
                    {
                        state.IsAlerting = true;

                        // The event's Value records how many minutes the machine had been
                        // silent when it fired. Subtracting that from its timestamp gives
                        // back the moment of the last reading before it went quiet.
                        state.SilentSinceUtc = lastAlert.TimestampUtc.AddMinutes(-lastAlert.Value);
                    }
                    else
                    {
                        // The last event names the level still in force: easing from critical
                        // is written as a warning being open precisely so this line can trust
                        // it. The consecutive counts restart from zero, which errs towards not
                        // repeating an announcement that has already gone out.
                        state.Threshold = new ThresholdState(lastAlert.Severity, 0, 0);
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
            .Select(s => new { s.Id, s.Name, s.LastSeenUtc, s.OfflineAfterSeconds })
            .ToListAsync(cancellationToken);

        var nowUtc = DateTime.UtcNow;

        foreach (var server in servers)
        {
            // Resolved per machine, through the same method the fleet screen uses: a laptop allowed
            // to sleep for twelve hours must not be reported missing after five minutes, and the
            // interface and the alert must never disagree about which rule applied.
            var offlineAfter = ServerHealthCalculator.ResolveOfflineAfter(
                server.OfflineAfterSeconds, settings.OfflineAfterSeconds);

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
                latest.CpuUsagePercent, settings.CpuWarningThreshold, settings.CpuThreshold,
                cancellationToken);

            await CheckThresholdAsync(dbContext, server.Id, server.Name, MetricKind.Memory,
                latest.MemoryUsagePercent, settings.MemoryWarningThreshold, settings.MemoryThreshold,
                cancellationToken);

            await CheckThresholdAsync(dbContext, server.Id, server.Name, MetricKind.Disk,
                latest.DiskUsagePercent, settings.DiskWarningThreshold, settings.DiskThreshold,
                cancellationToken);
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
                silentMinutes, thresholdMinutes, AlertSeverity.Critical, cancellationToken);

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
            downtimeMinutes, thresholdMinutes, AlertSeverity.Critical, cancellationToken);
    }

    private async Task CheckThresholdAsync(
        AppDbContext dbContext,
        int serverId,
        string serverName,
        MetricKind kind,
        double value,
        double warningThreshold,
        double criticalThreshold,
        CancellationToken cancellationToken)
    {
        var state = StateFor(serverId, kind);

        // The decision lives in ThresholdRule, where it can be tested; this method only carries
        // its result to the journal and the channels.
        var (next, change) = ThresholdRule.Evaluate(
            state.Threshold, value, warningThreshold, criticalThreshold,
            _options.RequiredConsecutiveSamples);

        state.Threshold = next;

        if (change is { } happened)
        {
            await RaiseAsync(dbContext, serverId, serverName, kind, happened.Kind,
                Math.Round(value, 1), happened.Threshold, happened.Severity, cancellationToken);
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
        AlertSeverity severity,
        CancellationToken cancellationToken)
    {
        dbContext.Alerts.Add(new Alert
        {
            ServerId = serverId,
            TimestampUtc = DateTime.UtcNow,
            MetricType = kind,
            Value = Math.Round(value, 1),
            Threshold = threshold,
            AlertType = alertKind,
            Severity = severity
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var notification = new AlertNotification(serverName, kind, alertKind, value, threshold, severity);

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
