namespace ServerMonitor.Api.Dtos;

/// <summary>What an agent reports about itself on its first run.</summary>
public class AgentRegistrationRequest
{
    public string Hostname { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string? AgentVersion { get; set; }
}
