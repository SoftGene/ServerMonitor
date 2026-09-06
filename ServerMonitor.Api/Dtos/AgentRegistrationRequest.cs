namespace ServerMonitor.Api.Dtos;

/// <summary>Что агент сообщает о себе при первом запуске.</summary>
public class AgentRegistrationRequest
{
    public string Hostname { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }
}
