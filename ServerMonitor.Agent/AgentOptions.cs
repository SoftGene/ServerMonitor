namespace ServerMonitor.Agent;

/// <summary>Настройки агента из секции "Agent" конфигурации.</summary>
public class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Адрес центрального API, например https://monitor.example.com.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Общий токен установки. Нужен только при первом запуске: после регистрации агент
    /// работает по персональному ключу, а токен больше не используется.
    /// </summary>
    public string EnrollmentToken { get; set; } = string.Empty;

    public int CollectIntervalSeconds { get; set; } = 5;

    /// <summary>Сколько замеров держать при недоступном сервере. 720 — примерно час.</summary>
    public int BufferCapacity { get; set; } = 720;

    /// <summary>Потолок паузы между повторами, чтобы не долбить лежащий сервер.</summary>
    public int MaxRetryDelaySeconds { get; set; } = 300;

    public TimeSpan CollectInterval => TimeSpan.FromSeconds(Math.Max(1, CollectIntervalSeconds));

    public TimeSpan MaxRetryDelay => TimeSpan.FromSeconds(Math.Max(1, MaxRetryDelaySeconds));
}
