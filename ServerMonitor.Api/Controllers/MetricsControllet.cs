using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;
using ServerMonitor.Api.Dtos;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public MetricsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("latest")]
    public async Task<ActionResult<MetricSnapshot>> GetLatest(CancellationToken cancellationToken)
    {
        var latest = await _dbContext.MetricSnapshots
            .OrderByDescending(m => m.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return NotFound("No metrics collected yet.");
        }

        return Ok(latest);
    }

    [HttpGet("status")]
    public async Task<ActionResult<ServerStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        var latest = await _dbContext.MetricSnapshots
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
            MemoryUsagePercent = latest.MemoryTotalMb > 0 ? Math.Round((latest.MemoryUsedMb / latest.MemoryTotalMb) * 100, 1) : 0,
            DiskUsedGb = Math.Round(latest.DiskUsedGb, 1),
            DiskTotalGb = Math.Round(latest.DiskTotalGb, 1),
            DiskUsagePercent = latest.DiskTotalGb > 0 ? Math.Round((latest.DiskUsedGb / latest.DiskTotalGb) * 100, 1) : 0,
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
        [FromQuery] int count = 50,
        CancellationToken cancellationToken = default)
    {
        if (count < 1 || count > 1000)
        {
            return BadRequest("Count must be between 1 and 1000.");
        }

        var items = await _dbContext.MetricSnapshots
            .OrderByDescending(m => m.TimestampUtc)
            .Take(count)
            .Select(m => new MetricHistoryItemDto
            {
                TimestampUtc = m.TimestampUtc,
                CpuUsagePercent = m.CpuUsagePercent,
                MemoryUsagePercent = m.MemoryTotalMb > 0 ? Math.Round(m.MemoryUsedMb / m.MemoryTotalMb * 100, 1) : 0,
                DiskUsagePercent = m.DiskTotalGb > 0 ? Math.Round(m.DiskUsedGb / m.DiskTotalGb * 100, 1) : 0
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}