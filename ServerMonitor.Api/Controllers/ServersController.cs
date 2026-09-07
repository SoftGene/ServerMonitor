using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/servers")]
public class ServersController : ControllerBase
{
    /// <summary>Сколько последних замеров отдавать на спарклайн в строке парка.</summary>
    private const int TrendPoints = 24;

    private readonly AppDbContext _dbContext;
    private readonly ILogger<ServersController> _logger;

    public ServersController(AppDbContext dbContext, ILogger<ServersController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<ServerSummaryDto>>> GetServers(CancellationToken cancellationToken)
    {
        var servers = await _dbContext.Servers
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var result = new List<ServerSummaryDto>(servers.Count);

        foreach (var server in servers)
        {
            result.Add(await BuildSummaryAsync(server, now, cancellationToken));
        }

        return Ok(result);
    }

    /// <summary>Одна машина — нужна детальной странице, чтобы показать имя и состояние в шапке.</summary>
    [HttpGet("{publicId:guid}")]
    public async Task<ActionResult<ServerSummaryDto>> GetServer(Guid publicId, CancellationToken cancellationToken)
    {
        var server = await _dbContext.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PublicId == publicId, cancellationToken);

        if (server is null)
        {
            return NotFound("Server not found.");
        }

        return Ok(await BuildSummaryAsync(server, DateTime.UtcNow, cancellationToken));
    }

    private async Task<ServerSummaryDto> BuildSummaryAsync(
        Domain.Entities.Server server,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // По отдельному запросу на сервер. Для парка из десятка машин это дёшево,
        // а вытащить последние N замеров сразу для всех одним запросом можно только
        // оконной функцией, то есть сырым SQL. Если парк вырастет — менять здесь.
        var recent = await _dbContext.MetricSnapshots
            .AsNoTracking()
            .Where(m => m.ServerId == server.Id)
            .OrderByDescending(m => m.TimestampUtc)
            .Take(TrendPoints)
            .ToListAsync(cancellationToken);

        // Замеры уже в памяти, поэтому вычисляемые свойства работают:
        // в проекции на стороне базы они бы не перевелись в SQL.
        var latest = recent.FirstOrDefault();

        return new ServerSummaryDto
        {
            PublicId = server.PublicId,
            Name = server.Name,
            OperatingSystem = server.OperatingSystem,
            AgentVersion = server.AgentVersion,
            LastSeenUtc = server.LastSeenUtc,
            Health = ServerHealthCalculator.FromLastSeen(server.LastSeenUtc, nowUtc).ToString(),
            CpuUsagePercent = latest?.CpuUsagePercent,
            MemoryUsagePercent = latest?.MemoryUsagePercent,
            DiskUsagePercent = latest?.DiskUsagePercent,
            // Запрос отдал свежие сверху, а графику нужно слева направо по времени.
            CpuTrend = recent
                .OrderBy(m => m.TimestampUtc)
                .Select(m => m.CpuUsagePercent)
                .ToList()
        };
    }

    [HttpDelete("{publicId:guid}")]
    public async Task<IActionResult> DeleteServer(Guid publicId, CancellationToken cancellationToken)
    {
        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.PublicId == publicId, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        // Каскад, описанный в модели, снесёт вместе с сервером все его замеры и алерты.
        // Операция необратимая, поэтому в интерфейсе она под подтверждением с вводом имени.
        _dbContext.Servers.Remove(server);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Server {Name} ({PublicId}) was deleted together with its whole history.",
            server.Name,
            publicId);

        return NoContent();
    }
}
