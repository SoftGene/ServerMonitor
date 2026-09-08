namespace ServerMonitor.Infrastructure.Auth;

/// <summary>
/// Счётчик неудачных входов. После нескольких промахов подряд имя временно блокируется.
/// </summary>
/// <remarks>
/// Считаем по <b>имени пользователя</b>, а не по IP-адресу. За одним адресом может сидеть
/// целый дом или офис, и блокировка по нему превратилась бы в отказ в обслуживании для
/// соседей. Обратная сторона выбора названа честно: зная логин, посторонний может временно
/// закрыть вход владельцу. Для домашней системы это приемлемая цена, для публичного сервиса
/// понадобилась бы связка «имя + адрес» и капча.
///
/// Время приходит параметром — тот же приём, что в HeartbeatRule: иначе поведение блокировки
/// нельзя было бы проверить тестами, не засыпая на пять минут.
///
/// Состояние живёт в памяти. После перезапуска счётчики обнуляются, и это осознанно: хранить
/// их в базе означало бы запись на каждую неудачную попытку — удобная мишень для того, кто
/// захочет нагрузить диск чужого сервера.
/// </remarks>
public class LoginThrottle
{
    private sealed class Attempts
    {
        public int Failures { get; set; }
        public DateTime? BlockedUntilUtc { get; set; }
    }

    private readonly Dictionary<string, Attempts> _attempts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _sync = new();
    private readonly int _maxAttempts;
    private readonly TimeSpan _blockFor;

    public LoginThrottle(int maxAttempts = 5, TimeSpan? blockFor = null)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _blockFor = blockFor ?? TimeSpan.FromMinutes(5);
    }

    public bool IsBlocked(string username, DateTime nowUtc)
    {
        lock (_sync)
        {
            if (!_attempts.TryGetValue(username, out var state) || state.BlockedUntilUtc is null)
            {
                return false;
            }

            if (state.BlockedUntilUtc > nowUtc)
            {
                return true;
            }

            // Срок вышел: снимаем блокировку и начинаем счёт заново, иначе следующая же
            // неудача снова заблокировала бы вход.
            _attempts.Remove(username);

            return false;
        }
    }

    public void RecordFailure(string username, DateTime nowUtc)
    {
        lock (_sync)
        {
            if (_attempts.TryGetValue(username, out var existing) &&
                existing.BlockedUntilUtc is { } until && until <= nowUtc)
            {
                _attempts.Remove(username);
            }

            if (!_attempts.TryGetValue(username, out var state))
            {
                state = new Attempts();
                _attempts[username] = state;
            }

            state.Failures++;

            if (state.Failures >= _maxAttempts)
            {
                state.BlockedUntilUtc = nowUtc + _blockFor;
            }
        }
    }

    /// <summary>Успешный вход стирает историю промахов.</summary>
    public void Reset(string username)
    {
        lock (_sync)
        {
            _attempts.Remove(username);
        }
    }
}
