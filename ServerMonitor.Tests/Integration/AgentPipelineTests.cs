using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// The path an agent actually takes: register once, then send readings with its own key.
/// </summary>
/// <remarks>
/// This is the flow that broke silently twice — a snapshot without a ServerId is fine C# and a
/// foreign key violation. Only a real database can catch that, which is the whole reason these
/// tests exist.
/// </remarks>
[Collection(ApiCollection.Name)]
public class AgentPipelineTests : IAsyncLifetime
{
    private readonly ApiFixture _api;

    public AgentPipelineTests(ApiFixture api)
    {
        _api = api;
    }

    public Task InitializeAsync() => _api.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Registration(Guid ServerId, string ApiKey);

    private async Task<Registration> RegisterAsync(string hostname, string? token = null)
    {
        using var client = _api.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Enrollment-Token", token ?? ApiFixture.EnrollmentToken);

        var response = await client.PostAsJsonAsync("/api/agents/register", new
        {
            hostname,
            operatingSystem = "Test OS",
            agentVersion = "1.0.0-test"
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Registration>())!;
    }

    [Fact]
    public async Task Registration_RefusesAWrongEnrollmentToken()
    {
        using var client = _api.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Enrollment-Token", "wrong");

        var response = await client.PostAsJsonAsync("/api/agents/register", new { hostname = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registration_StoresOnlyTheHashOfTheKey()
    {
        var registration = await RegisterAsync("hash-check");

        var stored = await _api.WithDbContextAsync(db => db.Servers
            .AsNoTracking()
            .Where(s => s.Name == "hash-check")
            .Select(s => s.ApiKeyHash)
            .FirstAsync());

        Assert.NotEmpty(stored);
        // A leaked database dump must not hand out working keys.
        Assert.DoesNotContain(registration.ApiKey, stored);
    }

    [Fact]
    public async Task Ingest_StoresReadingsAgainstTheRightServer()
    {
        var registration = await RegisterAsync("ingest-target");

        using var agent = _api.CreateAgentClient(registration.ApiKey);

        var response = await agent.PostAsJsonAsync("/api/ingest", new[]
        {
            new
            {
                timestampUtc = DateTime.UtcNow,
                cpuUsagePercent = 12.5,
                memoryUsedMb = 4096.0,
                memoryTotalMb = 16384.0,
                diskUsedGb = 100.0,
                diskTotalGb = 500.0,
                uptimeSeconds = 3600.0
            }
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var stored = await _api.WithDbContextAsync(db => db.MetricSnapshots
            .AsNoTracking()
            .Join(db.Servers, m => m.ServerId, s => s.Id, (m, s) => new { s.Name, m.CpuUsagePercent })
            .ToListAsync());

        // The foreign key is the point: a snapshot with no server would never have got here.
        var reading = Assert.Single(stored);
        Assert.Equal("ingest-target", reading.Name);
        Assert.Equal(12.5, reading.CpuUsagePercent);
    }

    [Fact]
    public async Task Ingest_RefusesAnUnknownKey()
    {
        using var agent = _api.CreateAgentClient("not-a-real-key");

        var response = await agent.PostAsJsonAsync("/api/ingest", new[]
        {
            new { timestampUtc = DateTime.UtcNow, cpuUsagePercent = 1.0 }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ingest_UpdatesLastSeen()
    {
        var registration = await RegisterAsync("last-seen");

        var before = await _api.WithDbContextAsync(db => db.Servers
            .AsNoTracking()
            .Where(s => s.Name == "last-seen")
            .Select(s => s.LastSeenUtc)
            .FirstAsync());

        Assert.Null(before);

        using var agent = _api.CreateAgentClient(registration.ApiKey);
        await agent.PostAsJsonAsync("/api/ingest", new[]
        {
            new
            {
                timestampUtc = DateTime.UtcNow,
                cpuUsagePercent = 5.0,
                memoryUsedMb = 1.0,
                memoryTotalMb = 2.0,
                diskUsedGb = 1.0,
                diskTotalGb = 2.0,
                uptimeSeconds = 1.0
            }
        });

        var after = await _api.WithDbContextAsync(db => db.Servers
            .AsNoTracking()
            .Where(s => s.Name == "last-seen")
            .Select(s => s.LastSeenUtc)
            .FirstAsync());

        // Health is derived from this column, so an ingest that does not touch it would make
        // a perfectly healthy machine drift into "offline".
        Assert.NotNull(after);
    }

    [Fact]
    public async Task ReadingsOfOneMachine_AreNotVisibleUnderAnother()
    {
        var first = await RegisterAsync("machine-one");
        var second = await RegisterAsync("machine-two");

        using var agent = _api.CreateAgentClient(first.ApiKey);
        await agent.PostAsJsonAsync("/api/ingest", new[]
        {
            new
            {
                timestampUtc = DateTime.UtcNow,
                cpuUsagePercent = 42.0,
                memoryUsedMb = 1.0,
                memoryTotalMb = 2.0,
                diskUsedGb = 1.0,
                diskTotalGb = 2.0,
                uptimeSeconds = 1.0
            }
        });

        using var client = _api.CreateServiceClient();

        var ownHistory = await client.GetFromJsonAsync<List<HistoryItem>>(
            $"/api/servers/{first.ServerId}/metrics/history?count=10");
        var otherHistory = await client.GetFromJsonAsync<List<HistoryItem>>(
            $"/api/servers/{second.ServerId}/metrics/history?count=10");

        Assert.Single(ownHistory!);
        Assert.Empty(otherHistory!);
    }

    [Fact]
    public async Task MetricsOfAnUnknownServer_Answer404()
    {
        using var client = _api.CreateServiceClient();

        var response = await client.GetAsync($"/api/servers/{Guid.NewGuid()}/metrics/status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletingAServer_TakesItsHistoryWithIt()
    {
        var registration = await RegisterAsync("doomed");

        using var agent = _api.CreateAgentClient(registration.ApiKey);
        await agent.PostAsJsonAsync("/api/ingest", new[]
        {
            new
            {
                timestampUtc = DateTime.UtcNow,
                cpuUsagePercent = 7.0,
                memoryUsedMb = 1.0,
                memoryTotalMb = 2.0,
                diskUsedGb = 1.0,
                diskTotalGb = 2.0,
                uptimeSeconds = 1.0
            }
        });

        using var client = _api.CreateServiceClient();
        var deleted = await client.DeleteAsync($"/api/servers/{registration.ServerId}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // The cascade is declared in the model; this is the test that it actually reached the
        // database schema rather than staying a good intention.
        var remaining = await _api.WithDbContextAsync(db => db.MetricSnapshots.CountAsync());

        Assert.Equal(0, remaining);
    }

    private sealed record HistoryItem(DateTime TimestampUtc, double CpuUsagePercent);
}
