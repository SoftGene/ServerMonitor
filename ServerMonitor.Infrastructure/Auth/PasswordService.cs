using Microsoft.AspNetCore.Identity;
using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Auth;

/// <summary>Результат проверки пароля.</summary>
public enum PasswordVerification
{
    Failed,
    Success,

    /// <summary>
    /// Пароль верный, но хеш сделан по устаревшим параметрам. Повод молча перезаписать его
    /// новым — пользователь ничего не заметит, а стойкость подрастёт.
    /// </summary>
    SuccessRehashNeeded
}

/// <summary>
/// Хеширование паролей поверх <see cref="PasswordHasher{TUser}"/> из ASP.NET Core.
/// </summary>
/// <remarks>
/// Здесь нужен намеренно <b>медленный</b> хеш, в отличие от ключей агентов (глава 10 гайда):
/// ключ — 32 случайных байта, словаря вероятных значений не существует, а пароль придумывает
/// человек, и перебор по словарю работает. PBKDF2 с десятками тысяч итераций делает такой
/// перебор непрактичным.
///
/// Своя реализация поверх Rfc2898DeriveBytes выглядит несложной, но у готового хешера есть
/// три вещи, которые легко забыть: случайная соль на каждый пароль, версия формата внутри
/// самого хеша и признак «пароль верный, но параметры устарели». Последнее и есть встроенный
/// путь миграции на более стойкие настройки без сброса паролей.
///
/// Обёртка нужна, чтобы остальной код не знал ни про ASP.NET Identity, ни про тип-параметр.
/// </remarks>
public class PasswordService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) =>
        _hasher.HashPassword(new User(), password);

    public PasswordVerification Verify(string storedHash, string password)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(password))
        {
            return PasswordVerification.Failed;
        }

        try
        {
            return _hasher.VerifyHashedPassword(new User(), storedHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordVerification.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
                _ => PasswordVerification.Failed
            };
        }
        catch (FormatException)
        {
            // В колонке оказался не хеш — например, после ручной правки базы. Это отказ во
            // входе, а не повод уронить приложение.
            return PasswordVerification.Failed;
        }
    }
}
