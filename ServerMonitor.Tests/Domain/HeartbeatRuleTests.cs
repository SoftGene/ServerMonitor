using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Tests.Domain;

public class HeartbeatRuleTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Offline = TimeSpan.FromMinutes(5);

    [Fact]
    public void NeverReported_IsNotAnAlert()
    {
        // Агент зарегистрировался, но ни разу не прислал данные — это незаконченная
        // установка, а не авария.
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: false, lastSeenUtc: null, Now, Offline));
    }

    [Fact]
    public void NeverReported_WhileAlerting_DoesNotRecover()
    {
        // Машина без данных не «выздоравливает» сама по себе.
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: true, lastSeenUtc: null, Now, Offline));
    }

    [Fact]
    public void FreshData_WhileHealthy_ChangesNothing()
    {
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: false, Now.AddSeconds(-10), Now, Offline));
    }

    [Fact]
    public void SilenceBeyondThreshold_Triggers()
    {
        Assert.Equal(
            AlertKind.Triggered,
            HeartbeatRule.Evaluate(isAlerting: false, Now.AddMinutes(-6), Now, Offline));
    }

    [Fact]
    public void SilenceContinuing_DoesNotRepeat()
    {
        // Сообщаем на переход, а не на состояние: иначе каждые полминуты приходило бы
        // «машина всё ещё недоступна».
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: true, Now.AddMinutes(-40), Now, Offline));
    }

    [Fact]
    public void DataReturns_Recovers()
    {
        Assert.Equal(
            AlertKind.Recovered,
            HeartbeatRule.Evaluate(isAlerting: true, Now.AddSeconds(-3), Now, Offline));
    }

    [Theory]
    [InlineData(299, null)]
    [InlineData(301, AlertKind.Triggered)]
    public void ThresholdBoundary(int secondsAgo, AlertKind? expected)
    {
        Assert.Equal(
            expected,
            HeartbeatRule.Evaluate(isAlerting: false, Now.AddSeconds(-secondsAgo), Now, Offline));
    }

    [Fact]
    public void ThresholdIsTakenFromTheArgument()
    {
        var lastSeen = Now.AddMinutes(-2);

        // Тот же момент времени — разный вердикт при разном пороге. Порог настраиваемый,
        // и правило обязано считаться именно с переданным значением.
        Assert.Null(HeartbeatRule.Evaluate(false, lastSeen, Now, TimeSpan.FromMinutes(5)));
        Assert.Equal(
            AlertKind.Triggered,
            HeartbeatRule.Evaluate(false, lastSeen, Now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ClockSkew_FutureTimestamp_IsTreatedAsFresh()
    {
        // Часы агента могут уйти вперёд. Замер «из будущего» — не повод объявлять машину
        // пропавшей: разница отрицательная, порог не превышен.
        Assert.Null(HeartbeatRule.Evaluate(isAlerting: false, Now.AddMinutes(10), Now, Offline));
    }
}
