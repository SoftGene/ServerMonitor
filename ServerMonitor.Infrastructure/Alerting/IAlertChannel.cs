namespace ServerMonitor.Infrastructure.Alerting;

/// <summary>
/// Where a notification goes. Several implementations can exist at once — they are all
/// registered in the container and all of them are called.
/// </summary>
/// <remarks>
/// A channel that is not configured must quietly do nothing rather than throw: having no
/// Telegram set up is a normal state of the system, not a failure. That confusion is exactly
/// why threshold checking used to stop working entirely without a bot token.
/// </remarks>
public interface IAlertChannel
{
    Task SendAsync(AlertNotification notification, CancellationToken cancellationToken);
}
