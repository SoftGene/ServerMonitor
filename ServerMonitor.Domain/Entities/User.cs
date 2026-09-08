namespace ServerMonitor.Domain.Entities;

/// <summary>
/// Учётная запись человека. Ролей нет: все учётки равны, разделение прав — отдельная задача,
/// до которой стоит дожить с реальной потребностью.
/// </summary>
public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Хеш пароля в самоописывающемся формате: внутри него лежат версия алгоритма, число
    /// итераций и соль. Благодаря этому параметры можно усилить, не сбрасывая пароли —
    /// старые хеши продолжат проверяться, а новые будут писаться по новым правилам.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastLoginUtc { get; set; }
}
