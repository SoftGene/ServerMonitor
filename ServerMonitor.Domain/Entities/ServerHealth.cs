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
    /// Состояние машины по свежести её данных. Пороги можно передать явно — их берут
    /// из настроек, чтобы экран парка и оповещения о пропаже судили по одной и той же
    /// границе. Без аргументов используются значения по умолчанию.
    /// </summary>
    public static ServerHealth FromLastSeen(
        DateTime? lastSeenUtc,
        DateTime nowUtc,
        TimeSpan? staleAfter = null,
        TimeSpan? offlineAfter = null)
    {
        if (lastSeenUtc is null)
        {
            return ServerHealth.Offline;
        }

        var stale = staleAfter ?? StaleAfter;
        var offline = offlineAfter ?? OfflineAfter;
        var age = nowUtc - lastSeenUtc.Value;

        if (age > offline)
        {
            return ServerHealth.Offline;
        }

        if (age > stale)
        {
            return ServerHealth.Stale;
        }

        return ServerHealth.Online;
    }
}
