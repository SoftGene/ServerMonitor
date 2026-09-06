using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerMonitor.Api.Dtos;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Agents;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Controllers;

[ApiController]
[Route("api/ingest")]
public class IngestController : ControllerBase
{
    /// <summary>
    /// Ограничение на размер пачки: агент досылает накопленный буфер, но клиент не должен
    /// иметь возможности прислать произвольно большой запрос.
    /// </summary>
    private const int MaxBatchSize = 1000;

    private readonly AppDbContext _dbContext;
    private readonly ILogger<IngestController> _logger;

    public IngestController(AppDbContext dbContext, ILogger<IngestController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Ingest(
        [FromBody] List<MetricReportDto> readings,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unauthorized();
        }

        // Ключ ищется по хешу: в базе открытого значения нет.
        var hash = ApiKeyGenerator.Hash(apiKey);

        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.ApiKeyHash == hash, cancellationToken);

        if (server is null)
        {
            _logger.LogWarning("Rejected ingest with an unknown API key.");

            return Unauthorized();
        }

        if (readings.Count == 0)
        {
            return BadRequest("At least one reading is required.");
        }

        if (readings.Count > MaxBatchSize)
        {
            return BadRequest($"At most {MaxBatchSize} readings per request.");
        }

        foreach (var reading in readings)
        {
            _dbContext.MetricSnapshots.Add(new MetricSnapshot
            {
                ServerId = server.Id,
                // Агент присылает UTC, но после разбора JSON пометка часового пояса может
                // потеряться, а колонка объявлена как timestamp with time zone.
                TimestampUtc = DateTime.SpecifyKind(reading.TimestampUtc, DateTimeKind.Utc),
                CpuUsagePercent = reading.CpuUsagePercent,
                MemoryUsedMb = reading.MemoryUsedMb,
                MemoryTotalMb = reading.MemoryTotalMb,
                DiskUsedGb = reading.DiskUsedGb,
                DiskTotalGb = reading.DiskTotalGb,
                UptimeSeconds = reading.UptimeSeconds
            });
        }

        server.LastSeenUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Accepted();
    }
}
