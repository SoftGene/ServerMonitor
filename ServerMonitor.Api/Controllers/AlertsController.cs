using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public AlertsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<List<AlertDto>>> GetAlerts(
        [FromQuery] int count = 50,
        CancellationToken cancellationToken = default)
    {
        if (count < 1 || count > 500)
        {
            return BadRequest("Count must be between 1 and 500.");
        }

        // Сначала материализуем записи, потом переводим перечисления в строки:
        // вызов ToDisplayName() в SQL не переводится.
        var alerts = await _dbContext.Alerts
            .AsNoTracking()
            .OrderByDescending(a => a.TimestampUtc)
            .Take(count)
            .ToListAsync(cancellationToken);

        var dtos = alerts
            .Select(a => new AlertDto
            {
                Id = a.Id,
                TimestampUtc = a.TimestampUtc,
                MetricType = a.MetricType.ToDisplayName(),
                Value = a.Value,
                Threshold = a.Threshold,
                AlertType = a.AlertType.ToString()
            })
            .ToList();

        return Ok(dtos);
    }
}
