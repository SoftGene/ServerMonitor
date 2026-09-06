namespace ServerMonitor.Domain.Entities;

/// <summary>Состояние сервера, выведенное из свежести пришедших данных.</summary>
public enum ServerHealth
{
    Online,
    Stale,
    Offline
}

public static class ServerHealthCalculator
{
    /// <summary>Данных нет дольше минуты — подозрительно.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    /// <summary>Дольше пяти минут — считаем сервер недоступным.</summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Пороги пока зашиты константами: настоящий детект падения с настройками и
    /// оповещениями — отдельный этап (heartbeat).
    /// </summary>
    public static ServerHealth FromLastSeen(DateTime? lastSeenUtc, DateTime nowUtc)
    {
        if (lastSeenUtc is null)
        {
            return ServerHealth.Offline;
        }

        var age = nowUtc - lastSeenUtc.Value;

        if (age > OfflineAfter)
        {
            return ServerHealth.Offline;
        }

        if (age > StaleAfter)
        {
            return ServerHealth.Stale;
        }

        return ServerHealth.Online;
    }
}
