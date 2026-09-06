using Microsoft.Extensions.Options;
using ServerMonitor.Collection;

namespace ServerMonitor.Agent;

/// <summary>
/// Цикл агента: снять замер, положить в буфер, попытаться отправить весь буфер.
/// Удалось — очищаем отправленное и спим обычный интервал; не удалось — оставляем в буфере
/// и увеличиваем паузу.
/// </summary>
public class AgentWorker : BackgroundService
{
    private readonly IMetricsCollector _collector;
    private readonly AgentClient _client;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentWorker> _logger;
    private readonly MetricBuffer _buffer;
    private readonly string _statePath;

    public AgentWorker(
        IMetricsCollector collector,
        AgentClient client,
        IOptions<AgentOptions> options,
        ILogger<AgentWorker> logger)
    {
        _collector = collector;
        _client = client;
        _options = options.Value;
        _logger = logger;
        _buffer = new MetricBuffer(_options.BufferCapacity);
        _statePath = Path.Combine(AppContext.BaseDirectory, "agent-state.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var state = await EnsureRegisteredAsync(stoppingToken);

        if (state is null)
        {
            // Без регистрации слать некуда. Сообщение уже записано в журнал.
            return;
        }

        var retryDelay = _options.CollectInterval;

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

                // Убираем ровно отправленное: за время запроса мог добавиться новый замер.
                _buffer.Remove(pending.Count);
                sent = true;

                if (pending.Count > 1)
                {
                    _logger.LogInformation("Sent {Count} buffered readings after reconnect.", pending.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Штатная остановка, а не сбой: выходим молча.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send readings; {Count} kept in the buffer.", _buffer.Count);
            }

            retryDelay = sent
                ? _options.CollectInterval
                : Min(retryDelay + retryDelay, _options.MaxRetryDelay);

            try
            {
                await Task.Delay(retryDelay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Agent stopped.");
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    /// <summary>
    /// Возвращает сохранённое состояние, а при первом запуске регистрируется по общему токену
    /// и сохраняет выданный ключ.
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
