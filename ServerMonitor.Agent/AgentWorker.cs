using Microsoft.Extensions.Options;
using ServerMonitor.Collection;

namespace ServerMonitor.Agent;

/// <summary>
/// The agent loop: take a reading, put it in the buffer, try to send the whole buffer.
/// On success, drop what was sent and sleep the normal interval; on failure, keep it in the
/// buffer and grow the delay.
/// </summary>
public class AgentWorker : BackgroundService
{
    private readonly IMetricsCollector _collector;
    private readonly AgentClient _client;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentWorker> _logger;
    private readonly MetricBuffer _buffer;
    private readonly BufferStore _bufferStore;
    private readonly string _statePath;

    /// <summary>
    /// The shortest gap between two writes of the buffer to disk.
    /// </summary>
    /// <remarks>
    /// Persisting on every reading would be a disk write every five seconds for a file that is
    /// almost always empty, since a reachable server empties the buffer immediately. Persisting
    /// only on shutdown would cover an orderly stop and nothing else — and the machine losing
    /// power is exactly the case worth surviving. Half a minute costs little and bounds the loss
    /// to the same half minute.
    /// </remarks>
    private static readonly TimeSpan MinimumSaveInterval = TimeSpan.FromSeconds(30);

    public AgentWorker(
        IMetricsCollector collector,
        AgentClient client,
        IOptions<AgentOptions> options,
        ILogger<AgentWorker> logger,
        ILogger<BufferStore> storeLogger)
    {
        _collector = collector;
        _client = client;
        _options = options.Value;
        _logger = logger;
        _buffer = new MetricBuffer(_options.BufferCapacity);
        _statePath = Path.Combine(AppContext.BaseDirectory, "agent-state.json");
        _bufferStore = new BufferStore(
            Path.Combine(AppContext.BaseDirectory, "agent-buffer.json"), storeLogger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var state = await EnsureRegisteredAsync(stoppingToken);

        if (state is null)
        {
            // With no registration there is nowhere to send. The reason is already logged.
            return;
        }

        var restored = await _bufferStore.LoadAsync(stoppingToken);

        if (restored.Count > 0)
        {
            _buffer.Restore(restored);

            _logger.LogInformation(
                "Recovered {Count} readings the previous run had not delivered.", restored.Count);
        }

        var retryDelay = _options.CollectInterval;
        var lastSavedUtc = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = false;

            try
            {
                var snapshot = await _collector.CollectAsync(stoppingToken);

                _buffer.Add(new MetricReport
                {
                    TimestampUtc = snapshot.TimestampUtc,
                    CpuUsagePercent = snapshot.CpuUsagePercent,
                    MemoryUsedMb = snapshot.MemoryUsedMb,
                    MemoryTotalMb = snapshot.MemoryTotalMb,
                    DiskUsedGb = snapshot.DiskUsedGb,
                    DiskTotalGb = snapshot.DiskTotalGb,
                    UptimeSeconds = snapshot.UptimeSeconds
                });

                var pending = _buffer.Snapshot();

                await _client.SendAsync(state.ApiKey, pending, stoppingToken);

                // Drop exactly what was sent: a new reading may have arrived mid-request.
                _buffer.Remove(pending.Count);
                sent = true;

                if (pending.Count > 1)
                {
                    _logger.LogInformation("Sent {Count} buffered readings after reconnect.", pending.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // An orderly shutdown rather than a failure: leave quietly.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send readings; {Count} kept in the buffer.", _buffer.Count);
            }

            retryDelay = sent
                ? _options.CollectInterval
                : Min(retryDelay + retryDelay, _options.MaxRetryDelay);

            // A delivered buffer is an empty one, and an empty buffer is no file: written
            // immediately, because leaving a stale file behind would make the next start replay
            // readings the server already has.
            if (_buffer.Count == 0)
            {
                _bufferStore.Delete();
            }
            else if (DateTime.UtcNow - lastSavedUtc >= MinimumSaveInterval)
            {
                await _bufferStore.SaveAsync(_buffer.Snapshot(), CancellationToken.None);
                lastSavedUtc = DateTime.UtcNow;
            }

            try
            {
                // Jitter the wait itself rather than the base: feeding a jittered value back
                // into the doubling would let the backoff drift away from its intended curve.
                await Task.Delay(sent ? retryDelay : WithJitter(retryDelay), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        // On the way out, keep whatever is left. The token is already cancelled by now, so the
        // write is given an uncancelled one — passing the cancelled token would abort the very
        // save this exists to perform.
        var remaining = _buffer.Snapshot();

        if (remaining.Count > 0)
        {
            await _bufferStore.SaveAsync(remaining, CancellationToken.None);

            _logger.LogInformation("Kept {Count} undelivered readings for the next run.", remaining.Count);
        }

        _logger.LogInformation("Agent stopped.");
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    /// <summary>
    /// Spreads the retry delay by up to 20% either way.
    /// </summary>
    /// <remarks>
    /// Without it, agents that lost the connection at the same moment retry at the same moment
    /// too, and a server coming back up is met by the whole fleet at once — the effect that
    /// keeps it from coming back. Randomising the delay turns a synchronised volley into a
    /// spread of arrivals.
    /// </remarks>
    private static TimeSpan WithJitter(TimeSpan delay)
    {
        var factor = 0.8 + Random.Shared.NextDouble() * 0.4;

        return TimeSpan.FromMilliseconds(delay.TotalMilliseconds * factor);
    }

    /// <summary>
    /// Returns the saved state, or on the first run registers with the shared token and
    /// stores the key it receives.
    /// </summary>
    private async Task<AgentState?> EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        var state = await AgentState.LoadAsync(_statePath, cancellationToken);

        if (state is not null)
        {
            _logger.LogInformation("Agent already registered as {ServerId}.", state.ServerId);

            return state;
        }

        if (string.IsNullOrWhiteSpace(_options.EnrollmentToken))
        {
            _logger.LogError(
                "Agent is not registered and Agent:EnrollmentToken is not configured. Nothing to do.");

            return null;
        }

        try
        {
            state = await _client.RegisterAsync(_options.EnrollmentToken, cancellationToken);

            await state.SaveAsync(_statePath, cancellationToken);

            _logger.LogInformation("Agent registered as {ServerId}.", state.ServerId);

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed.");

            return null;
        }
    }
}
