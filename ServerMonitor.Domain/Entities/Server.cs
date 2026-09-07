namespace ServerMonitor.Domain.Entities;

/// <summary>Наблюдаемая машина, на которой установлен агент.</summary>
public class Server
{
    public int Id { get; set; }

    /// <summary>
    /// Идентификатор для URL и API. Отдельно от <see cref="Id"/>: последовательные числа в
    /// адресах раскрывают количество серверов и позволяют перебирать чужие.
    /// </summary>
    public Guid PublicId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }

    /// <summary>
    /// SHA-256 персонального ключа агента. Пусто означает, что агент к записи ещё не привязан —
    /// такое состояние бывает только у записи, созданной миграцией для старой истории.
    /// </summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    public DateTime RegisteredAtUtc { get; set; }

    /// <summary>Когда от агента в последний раз приходили данные. По ней вычисляется статус.</summary>
    public DateTime? LastSeenUtc { get; set; }
}
