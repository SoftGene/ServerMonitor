using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Auth;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequireServiceKey]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public SettingsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<SettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.AppSettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
            return NotFound("Settings not found.");

        return Ok(new SettingsDto
        {
            CpuThreshold = settings.CpuThreshold,
            MemoryThreshold = settings.MemoryThreshold,
            DiskThreshold = settings.DiskThreshold,
            AlertsEnabled = settings.AlertsEnabled,
            OfflineAfterSeconds = settings.OfflineAfterSeconds,
            HeartbeatAlertsEnabled = settings.HeartbeatAlertsEnabled
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] SettingsDto dto, CancellationToken cancellationToken)
    {
        if (dto.CpuThreshold < 1 || dto.CpuThreshold > 100 ||
            dto.MemoryThreshold < 1 || dto.MemoryThreshold > 100 ||
            dto.DiskThreshold < 1 || dto.DiskThreshold > 100)
        {
            return BadRequest("Thresholds must be between 1 and 100.");
        }

        // The lower bound is not cosmetic: a threshold shorter than the collection interval
        // would mean a machine "disappears" between two perfectly normal readings.
        if (dto.OfflineAfterSeconds < 30 || dto.OfflineAfterSeconds > 86400)
        {
            return BadRequest("Offline threshold must be between 30 seconds and 24 hours.");
        }

        var settings = await _dbContext.AppSettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
            return NotFound("Settings not found.");

        settings.CpuThreshold = dto.CpuThreshold;
        settings.MemoryThreshold = dto.MemoryThreshold;
        settings.DiskThreshold = dto.DiskThreshold;
        settings.AlertsEnabled = dto.AlertsEnabled;
        settings.OfflineAfterSeconds = dto.OfflineAfterSeconds;
        settings.HeartbeatAlertsEnabled = dto.HeartbeatAlertsEnabled;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}