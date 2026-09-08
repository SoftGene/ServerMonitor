using Microsoft.AspNetCore.Identity;
using ServerMonitor.Domain.Entities;

namespace ServerMonitor.Infrastructure.Auth;

/// <summary>The outcome of verifying a password.</summary>
public enum PasswordVerification
{
    Failed,
    Success,

    /// <summary>
    /// The password is correct but its hash was made with outdated parameters. Reason enough
    /// to quietly rewrite it — the user notices nothing and the strength goes up.
    /// </summary>
    SuccessRehashNeeded
}

/// <summary>
/// Password hashing on top of <see cref="PasswordHasher{TUser}"/> from ASP.NET Core.
/// </summary>
/// <remarks>
/// A deliberately <b>slow</b> hash is what is needed here, unlike the agent keys: a key is 32
/// random bytes with no dictionary of likely values to try, whereas a password is chosen by a
/// person and a dictionary attack works. PBKDF2 with tens of thousands of iterations makes
/// that attack impractical.
///
/// Writing it by hand over Rfc2898DeriveBytes looks simple enough, but the ready-made hasher
/// does three things that are easy to forget: a fresh salt per password, a format version
/// stored inside the hash itself, and a "correct but outdated" result. The last of these is
/// the built-in path to stronger settings without resetting anyone's password.
///
/// The wrapper exists so the rest of the code knows about neither ASP.NET Identity nor its type parameter.
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
            // The column holds something that is not a hash — after a manual edit of the
            // database, say. That is a failed login, not a reason to bring the app down.
            return PasswordVerification.Failed;
        }
    }
}
