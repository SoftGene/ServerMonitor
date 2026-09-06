namespace ServerMonitor.Api.Dtos;

/// <summary>Ответ регистрации. Ключ отдаётся ровно один раз и больше не восстанавливается.</summary>
public class AgentRegistrationResponse
{
    public Guid ServerId { get; set; }
    public string ApiKey { get; set; } = string.Empty;
}
