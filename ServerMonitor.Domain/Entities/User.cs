namespace ServerMonitor.Domain.Entities;

/// <summary>
/// A person's account. There are no roles: every account is equal, and splitting permissions
/// is a separate job worth doing once there is a real need for it.
/// </summary>
public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The password hash in a self-describing format: the algorithm version, the iteration
    /// count and the salt all live inside it. That is what allows the parameters to be
    /// strengthened without resetting anyone's password — old hashes keep verifying while
    /// new ones are written under the new rules.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// A value that changes whenever every existing session for this account should stop being
    /// accepted.
    /// </summary>
    /// <remarks>
    /// A signed cookie is believed on its own signature: the server does not keep a list of who
    /// is currently signed in, which is exactly what makes cookie authentication cheap. The cost
    /// is that deleting an account, or changing its password, does nothing to a browser that is
    /// already holding a valid cookie — it stays signed in until the cookie expires, which here
    /// is a week.
    /// <para>
    /// The stamp is the usual answer. It is copied into the cookie when the session starts, and
    /// re-checked against this column periodically; once they disagree, the session is over.
    /// ASP.NET Identity calls the same thing a security stamp, and this is the same idea written
    /// out by hand.
    /// </para>
    /// </remarks>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastLoginUtc { get; set; }
}
