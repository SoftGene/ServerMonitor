using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
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
            AlertsEnabled = settings.AlertsEnabled
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

        var settings = await _dbContext.AppSettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
            return NotFound("Settings not found.");

        settings.CpuThreshold = dto.CpuThreshold;
        settings.MemoryThreshold = dto.MemoryThreshold;
        settings.DiskThreshold = dto.DiskThreshold;
        settings.AlertsEnabled = dto.AlertsEnabled;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}