using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Auth;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/servers")]
[RequireServiceKey]
public class ServersController : ControllerBase
{
    /// <summary>How many recent readings to return for the sparkline in a fleet row.</summary>
    private const int TrendPoints = 24;

    /// <summary>Longest name a machine may be given.</summary>
    private const int MaxNameLength = 60;

    /// <summary>Bounds for a machine's own silence threshold — the same as the fleet-wide setting.</summary>
    private const int MinOfflineSeconds = 30;
    private const int MaxOfflineSeconds = 86400;

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
        var offlineAfter = await ReadOfflineThresholdAsync(cancellationToken);
        var result = new List<ServerSummaryDto>(servers.Count);

        foreach (var server in servers)
        {
            result.Add(await BuildSummaryAsync(server, now, offlineAfter, cancellationToken));
        }

        return Ok(result);
    }

    /// <summary>A single machine — the detail page needs it for the name and health in its header.</summary>
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

        var offlineAfter = await ReadOfflineThresholdAsync(cancellationToken);

        return Ok(await BuildSummaryAsync(server, DateTime.UtcNow, offlineAfter, cancellationToken));
    }

    /// <summary>
    /// The "machine is missing" threshold from settings. The fleet screen and the alerting
    /// have to judge by the same boundary: were they to diverge, the interface would show a
    /// machine as healthy after the alert had already gone out.
    /// </summary>
    private async Task<TimeSpan> ReadOfflineThresholdAsync(CancellationToken cancellationToken)
    {
        var seconds = await _dbContext.AppSettings
            .AsNoTracking()
            .Select(a => (int?)a.OfflineAfterSeconds)
            .FirstOrDefaultAsync(cancellationToken);

        return seconds is null or <= 0
            ? ServerHealthCalculator.OfflineAfter
            : TimeSpan.FromSeconds(seconds.Value);
    }

    private async Task<ServerSummaryDto> BuildSummaryAsync(
        Domain.Entities.Server server,
        DateTime nowUtc,
        TimeSpan offlineAfter,
        CancellationToken cancellationToken)
    {
        // One query per server. For a fleet of a dozen machines that is cheap, and pulling
        // the last N readings for all of them at once would take a window function, which
        // means raw SQL. If the fleet grows, this is the place to change.
        var recent = await _dbContext.MetricSnapshots
            .AsNoTracking()
            .Where(m => m.ServerId == server.Id)
            .OrderByDescending(m => m.TimestampUtc)
            .Take(TrendPoints)
            .ToListAsync(cancellationToken);

        // The readings are already in memory, so the computed properties work here; in a
        // projection on the database side they would not translate to SQL.
        var latest = recent.FirstOrDefault();

        return new ServerSummaryDto
        {
            PublicId = server.PublicId,
            Name = server.Name,
            OperatingSystem = server.OperatingSystem,
            AgentVersion = server.AgentVersion,
            LastSeenUtc = server.LastSeenUtc,
            // Through the same resolver the alerting uses, so the screen and the notification can
            // never disagree about whether this machine is missing.
            Health = ServerHealthCalculator
                .FromLastSeen(
                    server.LastSeenUtc,
                    nowUtc,
                    offlineAfter: ServerHealthCalculator.ResolveOfflineAfter(
                        server.OfflineAfterSeconds, (int)offlineAfter.TotalSeconds))
                .ToString(),
            CustomOfflineAfterSeconds = server.OfflineAfterSeconds,
            FleetOfflineAfterSeconds = (int)offlineAfter.TotalSeconds,
            CpuUsagePercent = latest?.CpuUsagePercent,
            MemoryUsagePercent = latest?.MemoryUsagePercent,
            DiskUsagePercent = latest?.DiskUsagePercent,
            // The query returned newest first, but the chart needs left to right in time.
            CpuTrend = recent
                .OrderBy(m => m.TimestampUtc)
                .Select(m => m.CpuUsagePercent)
                .ToList()
        };
    }

    /// <summary>Renames a machine.</summary>
    /// <remarks>
    /// The name is the one thing about a machine a person chooses. It starts as the hostname the
    /// agent reported, which is right as a default and often wrong as a label — "DESKTOP-5FCP59V"
    /// says nothing about what the box actually does.
    /// <para>
    /// Safe to keep: the name is written only when an agent first registers, so a renamed machine
    /// stays renamed no matter how many readings arrive afterwards.
    /// </para>
    /// </remarks>
    [HttpPatch("{publicId:guid}")]
    public async Task<IActionResult> RenameServer(
        Guid publicId,
        [FromBody] RenameServerRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            return BadRequest("A machine needs a name.");
        }

        // Long enough for anything descriptive, short enough that one row cannot wreck the fleet
        // layout for every other machine.
        if (name.Length > MaxNameLength)
        {
            return BadRequest($"A name may be at most {MaxNameLength} characters.");
        }

        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.PublicId == publicId, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        // Names are deliberately not unique. Two machines may genuinely be called "backup", and
        // refusing that would be the interface inventing a rule the system does not have — the
        // identity of a machine is its PublicId, which is what every link and query uses.
        var previous = server.Name;
        server.Name = name;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Server {PublicId} renamed from {Previous} to {Name}.", publicId, previous, name);

        return NoContent();
    }

    /// <summary>
    /// Sets how long this machine may stay silent before it counts as missing, or returns it to
    /// the fleet default when the value is null.
    /// </summary>
    /// <remarks>
    /// Its own endpoint rather than a field on the rename. In JSON "absent" and "null" are easy to
    /// confuse, and here null carries a meaning of its own — "stop overriding". A separate address
    /// keeps that meaning impossible to misread.
    /// </remarks>
    [HttpPut("{publicId:guid}/offline-threshold")]
    public async Task<IActionResult> SetOfflineThreshold(
        Guid publicId,
        [FromBody] SetOfflineThresholdRequest request,
        CancellationToken cancellationToken)
    {
        // The same bounds as the fleet-wide setting, for the same reason: below the collection
        // interval a perfectly healthy machine would "disappear" between two normal readings.
        if (request.Seconds is < MinOfflineSeconds or > MaxOfflineSeconds)
        {
            return BadRequest(
                $"Offline threshold must be between {MinOfflineSeconds} seconds and {MaxOfflineSeconds / 3600} hours.");
        }

        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.PublicId == publicId, cancellationToken);

        if (server is null)
        {
            return NotFound();
        }

        server.OfflineAfterSeconds = request.Seconds;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (request.Seconds is null)
        {
            _logger.LogInformation("Server {PublicId} now follows the fleet offline threshold.", publicId);
        }
        else
        {
            _logger.LogInformation(
                "Server {PublicId} offline threshold set to {Seconds} s.", publicId, request.Seconds);
        }

        return NoContent();
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

        // The cascade declared in the model takes every reading and alert down with the
        // server. The operation is irreversible, which is why the UI puts it behind typing
        // the machine's name.
        _dbContext.Servers.Remove(server);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Server {Name} ({PublicId}) was deleted together with its whole history.",
            server.Name,
            publicId);

        return NoContent();
    }
}
