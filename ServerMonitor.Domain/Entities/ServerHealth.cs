namespace ServerMonitor.Domain.Entities;

/// <summary>Machine state, derived from how fresh its data is.</summary>
public enum ServerHealth
{
    Online,
    Stale,
    Offline
}

public static class ServerHealthCalculator
{
    /// <summary>No data for over a minute is suspicious.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    /// <summary>Over five minutes and the machine counts as unreachable.</summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Machine health from the age of its data. The thresholds can be passed explicitly —
    /// they come from settings, so the fleet screen and the availability alerts judge by the
    /// same boundary. Without arguments the defaults above apply.
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
