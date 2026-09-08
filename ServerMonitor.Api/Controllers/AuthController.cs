using Microsoft.AspNetCore.Mvc;
using ServerMonitor.Api.Auth;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Infrastructure.Auth;

namespace ServerMonitor.Api.Controllers;

/// <summary>
/// Проверка учётных данных и управление учётками. Сессий здесь нет: их держит веб-приложение
/// своим cookie, а API только отвечает «пара верна» или «нет».
/// </summary>
[ApiController]
[Route("api/auth")]
[RequireServiceKey]
public class AuthController : ControllerBase
{
    /// <summary>Минимальная длина пароля. Восемь — не идеал, но заметно лучше умолчания «никакой».</summary>
    private const int MinPasswordLength = 8;

    private readonly UserService _users;
    private readonly ILogger<AuthController> _logger;

    public AuthController(UserService users, ILogger<AuthController> logger)
    {
        _users = users;
        _logger = logger;
    }

    [HttpGet("state")]
    public async Task<ActionResult<AuthStateDto>> GetState(CancellationToken cancellationToken)
    {
        return Ok(new AuthStateDto { HasUsers = await _users.AnyUsersAsync(cancellationToken) });
    }

    /// <summary>Создаёт первую учётку. Работает, только пока таблица пуста.</summary>
    [HttpPost("setup")]
    public async Task<ActionResult<UserDto>> Setup(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        // Без этой проверки эндпоинт был бы открытой регистрацией администратора.
        if (await _users.AnyUsersAsync(cancellationToken))
        {
            _logger.LogWarning("Setup attempted after the first account already exists.");

            return Conflict("Setup has already been completed.");
        }

        var problem = Validate(request);

        if (problem is not null)
        {
            return BadRequest(problem);
        }

        var user = await _users.CreateAsync(request.Username.Trim(), request.Password, cancellationToken);

        return Ok(ToDto(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Unauthorized();
        }

        var result = await _users.VerifyAsync(request.Username.Trim(), request.Password, cancellationToken);

        if (!result.Succeeded)
        {
            // Один и тот же ответ на все причины отказа: подробности выдали бы, какие логины
            // существуют и не заблокирован ли перебор. Настоящая причина — в журнале.
            return Unauthorized();
        }

        return Ok(new LoginResponse { Username = result.User!.Username });
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<UserDto>>> GetUsers(CancellationToken cancellationToken)
    {
        var users = await _users.ListAsync(cancellationToken);

        return Ok(users.Select(ToDto).ToList());
    }

    [HttpPost("users")]
    public async Task<ActionResult<UserDto>> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var problem = Validate(request);

        if (problem is not null)
        {
            return BadRequest(problem);
        }

        var username = request.Username.Trim();
        var existing = await _users.ListAsync(cancellationToken);

        if (existing.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict("An account with this name already exists.");
        }

        var user = await _users.CreateAsync(username, request.Password, cancellationToken);

        return Ok(ToDto(user));
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id, CancellationToken cancellationToken)
    {
        // Отказ означает «это последняя учётка» либо «такой нет» — удалять последнюю нельзя,
        // иначе войти будет некому.
        return await _users.DeleteAsync(id, cancellationToken)
            ? NoContent()
            : BadRequest("Cannot delete this account. The last remaining account must stay.");
    }

    private static string? Validate(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return "Username is required.";
        }

        if (request.Username.Trim().Length > 64)
        {
            return "Username must be 64 characters or fewer.";
        }

        if (request.Password.Length < MinPasswordLength)
        {
            return $"Password must be at least {MinPasswordLength} characters.";
        }

        return null;
    }

    private static UserDto ToDto(Domain.Entities.User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        CreatedAtUtc = user.CreatedAtUtc,
        LastLoginUtc = user.LastLoginUtc
    };
}
