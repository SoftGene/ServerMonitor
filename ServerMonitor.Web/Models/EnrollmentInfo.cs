namespace ServerMonitor.Web.Models;

/// <summary>Mirrors EnrollmentInfoDto from the API.</summary>
public class EnrollmentInfo
{
    /// <summary>Null when the server has agent registration switched off.</summary>
    public string? Token { get; set; }
}
