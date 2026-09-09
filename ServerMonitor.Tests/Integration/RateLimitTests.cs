using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// Guessing a shared secret has to be slow.
/// </summary>
/// <remarks>
/// The login endpoint already counts failures per username. Nothing counted attempts at the
/// enrollment token, which is one shared value that never expires — so the only thing standing
/// between an attacker and it was how fast they could send requests.
/// <para>
/// These build their own host, because the limits have to be low enough to reach in a test and a
/// running host cannot have them changed. The database is the shared container's.
/// </para>
/// </remarks>
[Collection(ApiCollection.Name)]
public class RateLimitTests : IAsyncLifetime
{
    private readonly ApiFixture _api;
    private WebApplicationFactory<Program> _factory = default!;

    public RateLimitTests(ApiFixture api)
    {
        _api = api;
    }

    public async Task InitializeAsync()
    {
        await _api.ResetAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _api.ConnectionString);
            builder.UseSetting("Api:ServiceKey", ApiFixture.ServiceKey);
            builder.UseSetting("Agents:EnrollmentToken", ApiFixture.EnrollmentToken);
            builder.UseSetting("Telegram:BotToken", string.Empty);
            builder.UseSetting("Telegram:ChatId", string.Empty);

            builder.UseSetting("RateLimits:EnrollmentPerHour", "3");
            builder.UseSetting("RateLimits:CredentialsPerMinute", "3");
        });
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient EnrollmentClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Enrollment-Token", "wrong-token");

        return client;
    }

    [Fact]
    public async Task GuessingTheEnrollmentToken_IsCutOff()
    {
        using var client = EnrollmentClient();

        // The first three are refused on their merits.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var refused = await client.PostAsJsonAsync("/api/agents/register", new { hostname = "guess" });

            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/agents/register", new { hostname = "guess" });

        // 429 rather than 503: the caller is being told to slow down, not that the service is
        // unavailable. One invites a retry later, the other invites a failover.
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task TheLimitCountsSuccessesToo()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Enrollment-Token", ApiFixture.EnrollmentToken);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var registered = await client.PostAsJsonAsync("/api/agents/register",
                new { hostname = $"machine-{attempt}" });

            registered.EnsureSuccessStatusCode();
        }

        var blocked = await client.PostAsJsonAsync("/api/agents/register", new { hostname = "one-too-many" });

        // Counting only failures would let an attacker who has guessed the token register without
        // limit — and a legitimate fleet rollout is the one case worth raising the setting for.
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task RepeatedLoginAttempts_AreCutOff()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", ApiFixture.ServiceKey);

        await client.PostAsJsonAsync("/api/auth/setup",
            new { username = "owner", password = "long-enough-password" });

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await client.PostAsJsonAsync("/api/auth/login",
                new { username = $"guess-{attempt}", password = "whatever" });
        }

        var blocked = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "guess-again", password = "whatever" });

        // This is the case the per-username throttle cannot see: a different username every time
        // means every counter stays at one. The two limits cover different attacks.
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task OrdinaryReads_AreNotRateLimited()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", ApiFixture.ServiceKey);

        // Far past the limit on the guarded endpoints. The dashboard polls, and a limiter applied
        // everywhere would throttle the thing this exists to protect.
        for (var request = 0; request < 12; request++)
        {
            var response = await client.GetAsync("/api/servers");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Ingest_IsNotRateLimited()
    {
        using var register = _factory.CreateClient();
        register.DefaultRequestHeaders.Add("X-Enrollment-Token", ApiFixture.EnrollmentToken);

        var response = await register.PostAsJsonAsync("/api/agents/register", new { hostname = "reporter" });
        response.EnsureSuccessStatusCode();

        var registration = await response.Content.ReadFromJsonAsync<Registration>();

        using var agent = _factory.CreateClient();
        agent.DefaultRequestHeaders.Add("X-Api-Key", registration!.ApiKey);

        // An agent already holds a key it was given; limiting it would only drop readings from
        // machines that are behaving. Rate limiting belongs on the endpoints that accept guesses.
        for (var batch = 0; batch < 8; batch++)
        {
            var sent = await agent.PostAsJsonAsync("/api/ingest", new[]
            {
                new
                {
                    timestampUtc = DateTime.UtcNow,
                    cpuUsagePercent = 1.0,
                    memoryUsedMb = 1.0,
                    memoryTotalMb = 2.0,
                    diskUsedGb = 1.0,
                    diskTotalGb = 2.0,
                    uptimeSeconds = 1.0
                }
            });

            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        }
    }

    private sealed record Registration(Guid ServerId, string ApiKey);
}
