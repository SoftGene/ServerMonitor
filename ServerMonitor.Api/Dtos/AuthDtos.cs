namespace ServerMonitor.Api.Dtos;

/// <summary>Состояние системы до входа: заведена ли хоть одна учётка.</summary>
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
/// Ответ на успешный вход. Ничего секретного здесь нет: сессию держит веб-приложение
/// своим cookie, API лишь подтверждает, что пара логин-пароль верна.
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
