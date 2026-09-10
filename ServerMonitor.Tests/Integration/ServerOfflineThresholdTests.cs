using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// A silence threshold per machine, as the API applies it.
/// </summary>
/// <remarks>
/// One threshold for the whole fleet judged a database server and a laptop that sleeps at night by
/// the same length of silence. These put a machine into a known state of silence directly in the
/// database — no agent, no waiting — and ask the API what it makes of it.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ServerOfflineThresholdTests : IAsyncLifetime
{
    private const int FleetDefaultSeconds = 300;
    private const int TwelveHours = 43200;

    private readonly ApiFixture _api;

    public ServerOfflineThresholdTests(ApiFixture api)
    {
        _api = api;
    }

    public Task InitializeAsync() => _api.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Registration(Guid ServerId, string ApiKey);

    private sealed record Summary(
        Guid PublicId,
        string Name,
        string Health,
        int? CustomOfflineAfterSeconds,
        int FleetOfflineAfterSeconds);

    /// <summary>Registers a machine and makes its last reading as old as requested.</summary>
    private async Task<Guid> MachineSilentForAsync(string hostname, TimeSpan silence)
    {
        using var client = _api.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Enrollment-Token", ApiFixture.EnrollmentToken);

        var response = await client.PostAsJsonAsync("/api/agents/register", new
        {
            hostname,
            operatingSystem = "Test OS",
            agentVersion = "1.0.0-test"
        });

        response.EnsureSuccessStatusCode();

        var id = (await response.Content.ReadFromJsonAsync<Registration>())!.ServerId;
        var lastSeen = DateTime.UtcNow - silence;

        await _api.WithDbContextAsync(db => db.Servers
            .Where(s => s.PublicId == id)
            .ExecuteUpdateAsync(update => update.SetProperty(s => s.LastSeenUtc, lastSeen)));

        return id;
    }

    private static async Task<Summary> ReadAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Summary>($"/api/servers/{id}"))!;

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, Guid id, int? seconds) =>
        client.PutAsJsonAsync($"/api/servers/{id}/offline-threshold", new { seconds });

    [Fact]
    public async Task ANewMachine_FollowsTheFleetDefault()
    {
        var id = await MachineSilentForAsync("fresh", TimeSpan.FromSeconds(5));

        using var client = _api.CreateServiceClient();
        var summary = await ReadAsync(client, id);

        Assert.Null(summary.CustomOfflineAfterSeconds);
        Assert.Equal(FleetDefaultSeconds, summary.FleetOfflineAfterSeconds);
    }

    [Fact]
    public async Task AShortOwnThreshold_ReportsAQuietMachineSooner()
    {
        var id = await MachineSilentForAsync("database", TimeSpan.FromMinutes(3));

        using var client = _api.CreateServiceClient();

        // Three minutes is suspicious under the fleet's five, not yet missing.
        Assert.Equal("Stale", (await ReadAsync(client, id)).Health);

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(client, id, 60)).StatusCode);

        // A machine that should never be quiet for more than a minute is missing now.
        Assert.Equal("Offline", (await ReadAsync(client, id)).Health);
    }

    [Fact]
    public async Task ALongOwnThreshold_KeepsASleepingMachineFromReadingOffline()
    {
        var id = await MachineSilentForAsync("laptop", TimeSpan.FromHours(2));

        using var client = _api.CreateServiceClient();

        Assert.Equal("Offline", (await ReadAsync(client, id)).Health);

        await SetAsync(client, id, TwelveHours);

        var summary = await ReadAsync(client, id);

        // Two hours of silence is an ordinary evening for a laptop allowed twelve.
        Assert.NotEqual("Offline", summary.Health);
        Assert.Equal(TwelveHours, summary.CustomOfflineAfterSeconds);
    }

    [Fact]
    public async Task ClearingIt_ReturnsTheMachineToTheFleetDefault()
    {
        var id = await MachineSilentForAsync("laptop", TimeSpan.FromHours(2));

        using var client = _api.CreateServiceClient();

        await SetAsync(client, id, TwelveHours);
        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(client, id, null)).StatusCode);

        var summary = await ReadAsync(client, id);

        // Null means "stop overriding", which is the reason the operation has its own endpoint.
        Assert.Null(summary.CustomOfflineAfterSeconds);
        Assert.Equal("Offline", summary.Health);
    }

    [Fact]
    public async Task OneMachinesThreshold_DoesNotLeakIntoAnother()
    {
        var laptop = await MachineSilentForAsync("laptop", TimeSpan.FromHours(2));
        var server = await MachineSilentForAsync("server", TimeSpan.FromHours(2));

        using var client = _api.CreateServiceClient();

        await SetAsync(client, laptop, TwelveHours);

        Assert.NotEqual("Offline", (await ReadAsync(client, laptop)).Health);
        Assert.Equal("Offline", (await ReadAsync(client, server)).Health);
    }

    [Fact]
    public async Task TheFleetList_AgreesWithTheMachineScreen()
    {
        var id = await MachineSilentForAsync("laptop", TimeSpan.FromHours(2));

        using var client = _api.CreateServiceClient();
        await SetAsync(client, id, TwelveHours);

        var fleet = await client.GetFromJsonAsync<List<Summary>>("/api/servers");
        var row = Assert.Single(fleet!);

        // Two screens, one verdict. If these could differ, the fleet would show a machine missing
        // that its own page calls healthy.
        Assert.Equal((await ReadAsync(client, id)).Health, row.Health);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(86401)]
    public async Task AThresholdOutsideTheBounds_IsRefusedAndChangesNothing(int seconds)
    {
        var id = await MachineSilentForAsync("host", TimeSpan.FromSeconds(5));

        using var client = _api.CreateServiceClient();

        var response = await SetAsync(client, id, seconds);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await ReadAsync(client, id)).CustomOfflineAfterSeconds);
    }

    [Fact]
    public async Task AnUnknownMachine_Answers404()
    {
        using var client = _api.CreateServiceClient();

        var response = await SetAsync(client, Guid.NewGuid(), 600);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WithoutTheServiceKey_ItIsRefused()
    {
        var id = await MachineSilentForAsync("host", TimeSpan.FromSeconds(5));

        using var anonymous = _api.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SetAsync(anonymous, id, 600)).StatusCode);
    }
}
