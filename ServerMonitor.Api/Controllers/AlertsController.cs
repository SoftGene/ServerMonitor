using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Auth;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

/// <summary>
/// Alerts stay a fleet-wide list: the question "where did something break" is about every
/// machine at once. The serverId parameter narrows it to one.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[RequireServiceKey]
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
        [FromQuery] Guid? serverId = null,
        CancellationToken cancellationToken = default)
    {
        if (count < 1 || count > 500)
        {
            return BadRequest("Count must be between 1 and 500.");
        }

        // The join to servers is what puts a machine name in the list: Alert has no
        // navigation property, only a foreign key.
        var query = from alert in _dbContext.Alerts.AsNoTracking()
                    join server in _dbContext.Servers.AsNoTracking() on alert.ServerId equals server.Id
                    select new { Alert = alert, server.Name, server.PublicId };

        if (serverId is not null)
        {
            // Existence is checked separately: an empty answer for a server that does not
            // exist would read as "no alerts" when in fact there is no such machine.
            var serverExists = await _dbContext.Servers
                .AsNoTracking()
                .AnyAsync(s => s.PublicId == serverId, cancellationToken);

            if (!serverExists)
            {
                return NotFound("Server not found.");
            }

            query = query.Where(row => row.PublicId == serverId);
        }

        var rows = await query
            .OrderByDescending(row => row.Alert.TimestampUtc)
            .Take(count)
            .ToListAsync(cancellationToken);

        // Materialise the rows first and convert the enums afterwards: ToDisplayName() does
        // not translate to SQL.
        var dtos = rows
            .Select(row => new AlertDto
            {
                Id = row.Alert.Id,
                TimestampUtc = row.Alert.TimestampUtc,
                MetricType = row.Alert.MetricType.ToDisplayName(),
                Value = row.Alert.Value,
                Threshold = row.Alert.Threshold,
                AlertType = row.Alert.AlertType.ToString(),
                ServerName = row.Name,
                ServerPublicId = row.PublicId
            })
            .ToList();

        return Ok(dtos);
    }
}
