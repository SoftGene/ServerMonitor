using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Agents;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger<AgentsController> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AgentRegistrationResponse>> Register(
        [FromBody] AgentRegistrationRequest request,
        [FromHeader(Name = "X-Enrollment-Token")] string? enrollmentToken,
        CancellationToken cancellationToken)
    {
        var expectedToken = _configuration["Agents:EnrollmentToken"];

        // Токен не настроен — регистрация выключена. Принимать кого угодно молча нельзя.
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            _logger.LogError("Agent registration is disabled: Agents:EnrollmentToken is not configured.");

            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Agent registration is not configured.");
        }

        if (string.IsNullOrWhiteSpace(enrollmentToken) ||
            !ApiKeyGenerator.FixedTimeEquals(enrollmentToken, expectedToken))
        {
            _logger.LogWarning("Rejected agent registration with an invalid enrollment token.");

            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Hostname))
        {
            return BadRequest("Hostname is required.");
        }

        var (key, hash) = ApiKeyGenerator.Generate();

        var server = await AdoptOrCreateAsync(request, hash, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Agent registered for server {Name} ({PublicId}).", server.Name, server.PublicId);

        return Ok(new AgentRegistrationResponse
        {
            ServerId = server.PublicId,
            ApiKey = key
        });
    }

    /// <summary>
    /// Обычно регистрация создаёт новую запись. Единственное исключение — запись без ключа,
    /// которую оставила миграция: к ней привязана вся история, собранная до появления агентов.
    /// Если бы первый агент завёл рядом вторую запись, история разорвалась бы надвое.
    /// Ситуация возможна только один раз: получив ключ, запись перестаёт быть кандидатом.
    /// </summary>
    private async Task<Server> AdoptOrCreateAsync(
        AgentRegistrationRequest request,
        string apiKeyHash,
        CancellationToken cancellationToken)
    {
        var orphans = await _dbContext.Servers
            .Where(s => s.ApiKeyHash == string.Empty)
            .ToListAsync(cancellationToken);

        if (orphans.Count == 1)
        {
            var adopted = orphans[0];

            adopted.Name = request.Hostname;
            adopted.OperatingSystem = request.OperatingSystem;
            adopted.AgentVersion = request.AgentVersion;
            adopted.ApiKeyHash = apiKeyHash;

            _logger.LogInformation("Adopted the server record left by the migration.");

            return adopted;
        }

        var server = new Server
        {
            PublicId = Guid.NewGuid(),
            Name = request.Hostname,
            OperatingSystem = request.OperatingSystem,
            AgentVersion = request.AgentVersion,
            ApiKeyHash = apiKeyHash,
            RegisteredAtUtc = DateTime.UtcNow
        };

        _dbContext.Servers.Add(server);

        return server;
    }
}
