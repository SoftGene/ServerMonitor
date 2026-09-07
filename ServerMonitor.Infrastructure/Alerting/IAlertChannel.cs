namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>
/// Куда уходит уведомление. Реализаций может быть несколько сразу — они регистрируются
/// в контейнере и вызываются все.
/// </summary>
/// <remarks>
/// Канал, который не настроен, обязан молча ничего не делать, а не бросать исключение:
/// отсутствие настроенного Telegram — нормальное состояние системы, а не сбой. Именно
/// поэтому раньше без токена бота не работала вся проверка порогов целиком.
/// </remarks>
public interface IAlertChannel
{
    Task SendAsync(AlertNotification notification, CancellationToken cancellationToken);
}
