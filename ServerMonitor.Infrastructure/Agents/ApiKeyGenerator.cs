using System.Security.Cryptography;
using System.Text;

namespace ServerMonitor.Infrastructure.Agents;

/// <summary>Creation and checking of the keys agents use when sending metrics.</summary>
public static class ApiKeyGenerator
{
    /// <summary>
    /// Creates an agent key and its hash. The key is handed to the caller once, at
    /// registration; only the hash is stored, and the key cannot be recovered from it.
    /// </summary>
    public static (string Key, string Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);

        // base64url: the key travels in an HTTP header, where + / and = are unwelcome.
        var key = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return (key, Hash(key));
    }

    /// <summary>
    /// SHA-256 with no salt and no stretching.
    /// <para>
    /// This would be wrong for passwords, which are attacked with a dictionary and need a
    /// slow hash such as bcrypt. A key here is 32 random bytes: no dictionary exists and
    /// 2^256 possibilities cannot be searched. A slow hash would only add latency to every
    /// metric that arrives.
    /// </para>
    /// </summary>
    public static string Hash(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Constant-time comparison. An ordinary string comparison returns at the first
    /// difference, and the response time then reveals the secret one character at a time.
    /// </summary>
    public static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
