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
            CpuWarningThreshold = settings.CpuWarningThreshold,
            MemoryWarningThreshold = settings.MemoryWarningThreshold,
            DiskWarningThreshold = settings.DiskWarningThreshold,
            AlertsEnabled = settings.AlertsEnabled,
            OfflineAfterSeconds = settings.OfflineAfterSeconds,
            HeartbeatAlertsEnabled = settings.HeartbeatAlertsEnabled
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] SettingsDto dto, CancellationToken cancellationToken)
    {
        double[] thresholds =
        [
            dto.CpuThreshold, dto.MemoryThreshold, dto.DiskThreshold,
            dto.CpuWarningThreshold, dto.MemoryWarningThreshold, dto.DiskWarningThreshold
        ];

        if (thresholds.Any(threshold => threshold < 1 || threshold > 100))
        {
            return BadRequest("Thresholds must be between 1 and 100.");
        }

        // Equal is allowed and means "no warnings for this metric". Above is refused: a warning
        // set higher than critical could never fire, because every value past it is already
        // critical. Refusing it says so, instead of quietly accepting a setting that does nothing.
        if (dto.CpuWarningThreshold > dto.CpuThreshold ||
            dto.MemoryWarningThreshold > dto.MemoryThreshold ||
            dto.DiskWarningThreshold > dto.DiskThreshold)
        {
            return BadRequest("A warning threshold cannot be above its critical threshold.");
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
        settings.CpuWarningThreshold = dto.CpuWarningThreshold;
        settings.MemoryWarningThreshold = dto.MemoryWarningThreshold;
        settings.DiskWarningThreshold = dto.DiskWarningThreshold;
        settings.AlertsEnabled = dto.AlertsEnabled;
        settings.OfflineAfterSeconds = dto.OfflineAfterSeconds;
        settings.HeartbeatAlertsEnabled = dto.HeartbeatAlertsEnabled;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
