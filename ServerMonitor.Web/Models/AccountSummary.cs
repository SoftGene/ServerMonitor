namespace ServerMonitor.Web.Models;

/// <summary>An account in the settings list. Mirrors UserDto from the API — the only link is the shape of the JSON.</summary>
public class AccountSummary
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
}
