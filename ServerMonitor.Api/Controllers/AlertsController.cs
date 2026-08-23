using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Dtos;
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
        var alerts = await _dbContext.Alerts
            .OrderByDescending(a => a.TimestampUtc)
            .Take(count)
            .Select(a => new AlertDto
            {
                Id = a.Id,
                TimestampUtc = a.TimestampUtc,
                MetricType = a.MetricType,
                Value = a.Value,
                Threshold = a.Threshold,
                AlertType = a.AlertType
            })
            .ToListAsync(cancellationToken);

        return Ok(alerts);
    }
}