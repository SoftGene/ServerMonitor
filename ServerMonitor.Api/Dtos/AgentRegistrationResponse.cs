namespace ServerMonitor.Api.Dtos;

/// <summary>The registration reply. The key is handed out exactly once and cannot be recovered.</summary>
public class AgentRegistrationResponse
{
    public Guid ServerId { get; set; }
    public string ApiKey { get; set; } = string.Empty;
}
