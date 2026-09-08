namespace ServerMonitor.Web.Models;

/// <summary>Учётная запись в списке настроек. Повторяет UserDto из API — связь только по форме JSON.</summary>
public class AccountSummary
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
}
