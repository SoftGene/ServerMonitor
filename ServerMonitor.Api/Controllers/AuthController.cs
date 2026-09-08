using Microsoft.AspNetCore.Mvc;
using ServerMonitor.Api.Auth;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Infrastructure.Auth;

namespace ServerMonitor.Api.Controllers;

/// <summary>
/// Credential checking and account management. There are no sessions here: the web app holds
/// those in its cookie, and the API only answers whether a pair is valid.
/// </summary>
[ApiController]
[Route("api/auth")]
[RequireServiceKey]
public class AuthController : ControllerBase
{
    /// <summary>Minimum password length. Eight is no ideal, but far better than no minimum at all.</summary>
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

    /// <summary>Creates the first account. Works only while the table is empty.</summary>
    [HttpPost("setup")]
    public async Task<ActionResult<UserDto>> Setup(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        // Without this check the endpoint would be open registration for an administrator.
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
            // One answer for every reason to refuse: details would reveal which logins exist
            // and whether the throttle is engaged. The real reason goes to the log.
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
        // A refusal means either "this is the last account" or "no such account" — the last
        // one cannot go, or there would be nobody left who could sign in.
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
