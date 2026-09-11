namespace ServerMonitor.Api.Dtos;

/// <summary>What the web app needs to show a ready-made install command.</summary>
public class EnrollmentInfoDto
{
    /// <summary>The enrollment token, or null when agent registration is switched off.</summary>
    public string? Token { get; set; }
}
