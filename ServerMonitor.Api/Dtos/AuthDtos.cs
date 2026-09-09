namespace ServerMonitor.Api.Dtos;

/// <summary>The state of the system before sign-in: whether any account exists yet.</summary>
public class AuthStateDto
{
    public bool HasUsers { get; set; }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// The reply to a successful sign-in. Nothing secret here: the web app holds the session in
/// its cookie, and the API merely confirms that the username and password match.
/// </summary>
public class LoginResponse
{
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The value the web app puts in its cookie and presents again to keep the session alive.
    /// </summary>
    /// <remarks>
    /// Not a secret and not a credential: on its own it grants nothing, because every call that
    /// accepts it already needs the service key. It exists so a signed cookie can be revoked,
    /// which a signed cookie otherwise cannot be.
    /// </remarks>
    public string SecurityStamp { get; set; } = string.Empty;
}

/// <summary>Asks whether a session that started earlier should still be honoured.</summary>
public class ValidateSessionRequest
{
    public string Username { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
}

public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
}
