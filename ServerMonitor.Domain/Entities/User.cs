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

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastLoginUtc { get; set; }
}
