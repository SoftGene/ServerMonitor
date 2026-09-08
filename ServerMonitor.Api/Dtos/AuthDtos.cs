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
