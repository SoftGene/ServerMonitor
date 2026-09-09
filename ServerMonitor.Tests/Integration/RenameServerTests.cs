using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// Giving a machine a name a person chose.
/// </summary>
/// <remarks>
/// A machine arrives called whatever its hostname is, which is a correct default and a poor label:
/// "DESKTOP-5FCP59V" says nothing about what the box does. The interesting part is not the update
/// itself but that it survives — an agent reports every few seconds, and a rename that the next
/// reading quietly undid would be worse than no rename at all.
/// </remarks>
[Collection(ApiCollection.Name)]
public class RenameServerTests : IAsyncLifetime
{
    private readonly ApiFixture _api;

    public RenameServerTests(ApiFixture api)
    {
        _api = api;
    }

    public Task InitializeAsync() => _api.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Registration(Guid ServerId, string ApiKey);

    private async Task<Registration> RegisterAsync(string hostname)
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

        return (await response.Content.ReadFromJsonAsync<Registration>())!;
    }

    private static Task<HttpResponseMessage> RenameAsync(HttpClient client, Guid serverId, string name) =>
        client.PatchAsJsonAsync($"/api/servers/{serverId}", new { name });

    private Task<string> NameOfAsync(Guid publicId) => _api.WithDbContextAsync(db => db.Servers
        .AsNoTracking()
        .Where(s => s.PublicId == publicId)
        .Select(s => s.Name)
        .FirstAsync());

    private static object Reading() => new
    {
        timestampUtc = DateTime.UtcNow,
        cpuUsagePercent = 5.0,
        memoryUsedMb = 1.0,
        memoryTotalMb = 2.0,
        diskUsedGb = 1.0,
        diskTotalGb = 2.0,
        uptimeSeconds = 1.0
    };

    [Fact]
    public async Task ARenamedMachine_KeepsItsNewName()
    {
        var registration = await RegisterAsync("DESKTOP-5FCP59V");

        using var client = _api.CreateServiceClient();

        var response = await RenameAsync(client, registration.ServerId, "build server");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("build server", await NameOfAsync(registration.ServerId));
    }

    [Fact]
    public async Task TheNewName_SurvivesEveryReadingThatFollows()
    {
        var registration = await RegisterAsync("DESKTOP-5FCP59V");

        using var client = _api.CreateServiceClient();
        await RenameAsync(client, registration.ServerId, "build server");

        using var agent = _api.CreateAgentClient(registration.ApiKey);

        for (var batch = 0; batch < 3; batch++)
        {
            var sent = await agent.PostAsJsonAsync("/api/ingest", new[] { Reading() });

            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        }

        // The point of the whole feature. Ingest may update when a machine was last seen; it must
        // not touch what the machine is called, or the rename lasts until the next reading.
        Assert.Equal("build server", await NameOfAsync(registration.ServerId));
    }

    [Fact]
    public async Task SurroundingSpace_IsTrimmed()
    {
        var registration = await RegisterAsync("host");

        using var client = _api.CreateServiceClient();
        await RenameAsync(client, registration.ServerId, "   build server   ");

        Assert.Equal("build server", await NameOfAsync(registration.ServerId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyName_IsRefused(string name)
    {
        var registration = await RegisterAsync("host");

        using var client = _api.CreateServiceClient();

        var response = await RenameAsync(client, registration.ServerId, name);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Refused means unchanged, not blanked.
        Assert.Equal("host", await NameOfAsync(registration.ServerId));
    }

    [Fact]
    public async Task AnAbsurdlyLongName_IsRefused()
    {
        var registration = await RegisterAsync("host");

        using var client = _api.CreateServiceClient();

        var response = await RenameAsync(client, registration.ServerId, new string('x', 61));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("host", await NameOfAsync(registration.ServerId));
    }

    [Fact]
    public async Task TwoMachines_MayShareAName()
    {
        var first = await RegisterAsync("one");
        var second = await RegisterAsync("two");

        using var client = _api.CreateServiceClient();

        Assert.Equal(HttpStatusCode.NoContent, (await RenameAsync(client, first.ServerId, "backup")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await RenameAsync(client, second.ServerId, "backup")).StatusCode);

        // Two machines really can both be the backup box. Refusing that would be the interface
        // inventing a rule the system does not have: identity is the PublicId, not the label.
        Assert.Equal("backup", await NameOfAsync(first.ServerId));
        Assert.Equal("backup", await NameOfAsync(second.ServerId));
    }

    [Fact]
    public async Task RenamingAMachineThatDoesNotExist_Answers404()
    {
        using var client = _api.CreateServiceClient();

        var response = await RenameAsync(client, Guid.NewGuid(), "anything");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RenamingWithoutTheServiceKey_IsRefused()
    {
        var registration = await RegisterAsync("host");

        using var client = _api.CreateAnonymousClient();

        var response = await RenameAsync(client, registration.ServerId, "whatever");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("host", await NameOfAsync(registration.ServerId));
    }
}
