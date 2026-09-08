using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;
using ServerMonitor.Api.Auth;
using ServerMonitor.Api.Dtos;

namespace ServerMonitor.Api.Controllers;

/// <summary>
/// Reading the metrics of one machine. The route is nested under the server because a
/// reading without a machine has no meaning: it used to be implied, now it is named.
/// </summary>
[ApiController]
[Route("api/servers/{publicId:guid}/metrics")]
[RequireServiceKey]
public class MetricsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public MetricsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Resolves the public identifier to the internal key. Only PublicId ever goes out,
    /// while relations inside the database are built on the numeric Id.
    /// </summary>
    private async Task<int?> ResolveServerIdAsync(Guid publicId, CancellationToken cancellationToken)
    {
        return await _dbContext.Servers
            .AsNoTracking()
            .Where(s => s.PublicId == publicId)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    [HttpGet("status")]
    public async Task<ActionResult<ServerStatusDto>> GetStatus(
        Guid publicId,
        CancellationToken cancellationToken)
    {
        var serverId = await ResolveServerIdAsync(publicId, cancellationToken);

        if (serverId is null)
        {
            return NotFound("Server not found.");
        }

        var latest = await _dbContext.MetricSnapshots
            .AsNoTracking()
            .Where(m => m.ServerId == serverId)
            .OrderByDescending(m => m.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return NotFound("No metrics collected yet.");
        }

        var dto = new ServerStatusDto
        {
            TimeStampUtc = latest.TimestampUtc,
            CpuUsagePercent = latest.CpuUsagePercent,
            MemoryUsedMb = Math.Round(latest.MemoryUsedMb, 1),
            MemoryTotalMb = Math.Round(latest.MemoryTotalMb, 1),
            MemoryUsagePercent = latest.MemoryUsagePercent,
            DiskUsedGb = Math.Round(latest.DiskUsedGb, 1),
            DiskTotalGb = Math.Round(latest.DiskTotalGb, 1),
            DiskUsagePercent = latest.DiskUsagePercent,
            Uptime = FormatUpTime(latest.UptimeSeconds),
        };

        return Ok(dto);
    }

    private static string FormatUpTime(double totalSeconds)
    {
        var span = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<MetricHistoryItemDto>>> GetHistory(
        Guid publicId,
        [FromQuery] int count = 50,
        CancellationToken cancellationToken = default)
    {
        if (count < 1 || count > 1000)
        {
            return BadRequest("Count must be between 1 and 1000.");
        }

        var serverId = await ResolveServerIdAsync(publicId, cancellationToken);

        if (serverId is null)
        {
            return NotFound("Server not found.");
        }

        var items = await _dbContext.MetricSnapshots
            .AsNoTracking()
            .Where(m => m.ServerId == serverId)
            .OrderByDescending(m => m.TimestampUtc)
            .Take(count)
            .Select(m => new MetricHistoryItemDto
            {
                TimestampUtc = m.TimestampUtc,
                CpuUsagePercent = m.CpuUsagePercent,
                // m.MemoryUsagePercent cannot be used here: the projection runs on the
                // PostgreSQL side, and a computed C# property does not translate to SQL.
                MemoryUsagePercent = m.MemoryTotalMb > 0 ? Math.Round(m.MemoryUsedMb / m.MemoryTotalMb * 100, 1) : 0,
                DiskUsagePercent = m.DiskTotalGb > 0 ? Math.Round(m.DiskUsedGb / m.DiskTotalGb * 100, 1) : 0
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("history/paged")]
    public async Task<ActionResult<PagedResult<MetricHistoryItemDto>>> GetHistoryPaged(
        Guid publicId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "timestamp",
        [FromQuery] string sortDir = "desc",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var serverId = await ResolveServerIdAsync(publicId, cancellationToken);

        if (serverId is null)
        {
            return NotFound("Server not found.");
        }

        IQueryable<MetricSnapshot> query = _dbContext.MetricSnapshots
            .AsNoTracking()
            .Where(m => m.ServerId == serverId);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
            query = query.Where(m => m.TimestampUtc >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value, DateTimeKind.Utc).AddDays(1);
            query = query.Where(m => m.TimestampUtc < toUtc);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        bool ascending = sortDir.Equals("asc", StringComparison.OrdinalIgnoreCase);

        query = sortBy.ToLower() switch
        {
            "cpu" => ascending
                ? query.OrderBy(m => m.CpuUsagePercent)
                : query.OrderByDescending(m => m.CpuUsagePercent),
            "memory" => ascending
                ? query.OrderBy(m => m.MemoryUsedMb / m.MemoryTotalMb)
                : query.OrderByDescending(m => m.MemoryUsedMb / m.MemoryTotalMb),
            "disk" => ascending
                ? query.OrderBy(m => m.DiskUsedGb / m.DiskTotalGb)
                : query.OrderByDescending(m => m.DiskUsedGb / m.DiskTotalGb),
            _ => ascending
                ? query.OrderBy(m => m.TimestampUtc)
                : query.OrderByDescending(m => m.TimestampUtc)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MetricHistoryItemDto
            {
                TimestampUtc = m.TimestampUtc,
                CpuUsagePercent = m.CpuUsagePercent,
                // See the comment in GetHistory: the projection is computed in SQL.
                MemoryUsagePercent = m.MemoryTotalMb > 0
                    ? Math.Round(m.MemoryUsedMb / m.MemoryTotalMb * 100, 1)
                    : 0,
                DiskUsagePercent = m.DiskTotalGb > 0
                    ? Math.Round(m.DiskUsedGb / m.DiskTotalGb * 100, 1)
                    : 0
            })
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new PagedResult<MetricHistoryItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        });
    }
}
