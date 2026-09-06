using System.Security.Cryptography;
using System.Text;

namespace ServerMonitor.Infrastructure.Agents;

/// <summary>Создание и проверка ключей, которыми агенты подписывают отправку метрик.</summary>
public static class ApiKeyGenerator
{
    /// <summary>
    /// Создаёт ключ агента и его хеш. Ключ возвращается вызывающему один раз — при
    /// регистрации; в базе остаётся только хеш, восстановить из него ключ нельзя.
    /// </summary>
    public static (string Key, string Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);

        // base64url: ключ ездит в HTTP-заголовке, и символы + / = там лишние.
        var key = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return (key, Hash(key));
    }

    /// <summary>
    /// SHA-256 без соли и растяжения.
    /// <para>
    /// Для паролей так делать нельзя — их подбирают по словарю, и нужен медленный хеш вроде
    /// bcrypt. Здесь же ключ — 32 случайных байта: словаря не существует, а перебор 2^256
    /// вариантов невозможен. Медленный хеш дал бы только задержку на каждом приёме метрик.
    /// </para>
    /// </summary>
    public static string Hash(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Сравнение за постоянное время. Обычное сравнение строк выходит на первом различии,
    /// и по времени ответа можно посимвольно угадать секрет.
    /// </summary>
    public static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
