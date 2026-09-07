using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

/// <summary>
/// Алерты остались общим списком по всему парку: вопрос «где вообще что-то сломалось»
/// относится ко всем машинам сразу. Сузить до одной можно параметром serverId.
/// </summary>
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
        [FromQuery] Guid? serverId = null,
        CancellationToken cancellationToken = default)
    {
        if (count < 1 || count > 500)
        {
            return BadRequest("Count must be between 1 and 500.");
        }

        // Соединение с серверами нужно, чтобы в списке было видно имя машины:
        // навигационного свойства у Alert нет, связь описана только внешним ключом.
        var query = from alert in _dbContext.Alerts.AsNoTracking()
                    join server in _dbContext.Servers.AsNoTracking() on alert.ServerId equals server.Id
                    select new { Alert = alert, server.Name, server.PublicId };

        if (serverId is not null)
        {
            // Существование машины проверяем отдельно: пустой ответ для несуществующего
            // сервера выглядел бы как «алертов нет», хотя нет самого сервера.
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

        // Сначала материализуем записи, потом переводим перечисления в строки:
        // вызов ToDisplayName() в SQL не переводится.
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
