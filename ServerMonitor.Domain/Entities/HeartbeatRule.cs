namespace ServerMonitor.Domain.Entities;

/// <summary>
/// Решает, изменилось ли состояние доступности машины.
/// </summary>
/// <remarks>
/// Чистая функция: и текущее состояние, и время приходят параметрами, ничего не читается
/// из базы и не отправляется наружу. Только поэтому правило удаётся покрыть тестами —
/// «сейчас» в тесте задаётся константой, и результат не зависит от момента запуска.
/// </remarks>
public static class HeartbeatRule
{
    /// <param name="isAlerting">Тревога по этой машине уже открыта.</param>
    /// <param name="lastSeenUtc">Когда от машины последний раз приходили данные.</param>
    /// <param name="nowUtc">Момент проверки.</param>
    /// <param name="offlineAfter">Сколько молчания считать пропажей.</param>
    /// <returns>
    /// <c>Triggered</c> — машина только что признана недоступной, <c>Recovered</c> — снова
    /// отвечает, <c>null</c> — ничего не изменилось и сообщать не о чем.
    /// </returns>
    public static AlertKind? Evaluate(
        bool isAlerting,
        DateTime? lastSeenUtc,
        DateTime nowUtc,
        TimeSpan offlineAfter)
    {
        if (lastSeenUtc is null)
        {
            // Данных не было никогда: агент зарегистрировался, но ни разу не отчитался.
            // Это незаконченная установка, а не авария — ни поднимать тревогу, ни снимать
            // её не о чем.
            return null;
        }

        // Разница может оказаться отрицательной, если часы агента ушли вперёд. Такой замер
        // считается свежим, и это правильно: расхождение часов — не признак пропажи машины.
        var isSilent = nowUtc - lastSeenUtc.Value > offlineAfter;

        if (isSilent && !isAlerting)
        {
            return AlertKind.Triggered;
        }

        if (!isSilent && isAlerting)
        {
            return AlertKind.Recovered;
        }

        // Состояние не изменилось. Сообщаем на переход, а не на состояние — иначе каждую
        // проверку приходило бы «машина всё ещё недоступна».
        return null;
    }
}
