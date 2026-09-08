using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Tests.Domain;

public class ServerHealthTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NeverSeen_IsOffline()
    {
        // Сервер зарегистрировался, но не прислал ни одного замера.
        Assert.Equal(ServerHealth.Offline, ServerHealthCalculator.FromLastSeen(null, Now));
    }

    [Theory]
    [InlineData(0, ServerHealth.Online)]
    [InlineData(59, ServerHealth.Online)]
    [InlineData(61, ServerHealth.Stale)]
    [InlineData(299, ServerHealth.Stale)]
    [InlineData(301, ServerHealth.Offline)]
    public void HealthDependsOnDataAge(int secondsAgo, ServerHealth expected)
    {
        var lastSeen = Now.AddSeconds(-secondsAgo);

        Assert.Equal(expected, ServerHealthCalculator.FromLastSeen(lastSeen, Now));
    }

    [Fact]
    public void ExactBoundaries_StayInTheHealthierState()
    {
        // Сравнение строгое (>), поэтому ровно на границе состояние ещё не меняется.
        Assert.Equal(
            ServerHealth.Online,
            ServerHealthCalculator.FromLastSeen(Now - ServerHealthCalculator.StaleAfter, Now));

        Assert.Equal(
            ServerHealth.Stale,
            ServerHealthCalculator.FromLastSeen(Now - ServerHealthCalculator.OfflineAfter, Now));
    }

    [Fact]
    public void ClockSkew_DoesNotBreakTheCalculation()
    {
        // Метка из будущего (часы агента убежали вперёд) — возраст отрицательный,
        // сервер считается живым, а не упавшим.
        Assert.Equal(ServerHealth.Online, ServerHealthCalculator.FromLastSeen(Now.AddMinutes(10), Now));
    }

    [Fact]
    public void ExplicitThresholds_OverrideTheDefaults()
    {
        var lastSeen = Now.AddSeconds(-90);

        // По умолчанию 90 секунд молчания — это Stale.
        Assert.Equal(ServerHealth.Stale, ServerHealthCalculator.FromLastSeen(lastSeen, Now));

        // С более строгими порогами тот же момент времени означает уже Offline.
        Assert.Equal(
            ServerHealth.Offline,
            ServerHealthCalculator.FromLastSeen(
                lastSeen,
                Now,
                staleAfter: TimeSpan.FromSeconds(15),
                offlineAfter: TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void OnlyOneThresholdCanBeOverridden()
    {
        var lastSeen = Now.AddSeconds(-30);

        // Порог Stale поднят, порог Offline остался по умолчанию — 30 секунд ещё Online.
        Assert.Equal(
            ServerHealth.Online,
            ServerHealthCalculator.FromLastSeen(lastSeen, Now, staleAfter: TimeSpan.FromMinutes(2)));
    }
}
