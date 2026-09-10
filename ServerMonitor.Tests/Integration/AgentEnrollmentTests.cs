using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// The enrollment token, as the web app fetches it to build an install command.
/// </summary>
[Collection(ApiCollection.Name)]
public class AgentEnrollmentTests
{
    private readonly ApiFixture _api;

    public AgentEnrollmentTests(ApiFixture api)
    {
        _api = api;
    }

    private sealed record Info(string? Token);

    [Fact]
    public async Task WithTheServiceKey_TheTokenIsReturned()
    {
        using var client = _api.CreateServiceClient();

        var info = await client.GetFromJsonAsync<Info>("/api/agents/enrollment");

        Assert.Equal(ApiFixture.EnrollmentToken, info!.Token);
    }

    [Fact]
    public async Task WithoutTheServiceKey_ItIsRefused()
    {
        using var anonymous = _api.CreateAnonymousClient();

        var response = await anonymous.GetAsync("/api/agents/enrollment");

        // The token lets a machine join the fleet. Anyone on the network asking for it directly,
        // rather than through the signed-in web app, gets nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WhenRegistrationIsOff_TheTokenIsNull()
    {
        // Its own host, because the token cannot be unset on the shared one without breaking every
        // other test that registers an agent.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", _api.ConnectionString);
            builder.UseSetting("Api:ServiceKey", ApiFixture.ServiceKey);
            builder.UseSetting("Agents:EnrollmentToken", string.Empty);
            builder.UseSetting("Telegram:BotToken", string.Empty);
            builder.UseSetting("Telegram:ChatId", string.Empty);
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", ApiFixture.ServiceKey);

        var info = await client.GetFromJsonAsync<Info>("/api/agents/enrollment");

        // Null rather than an empty string, so the page can say plainly that registration is
        // switched off instead of showing a command that is guaranteed to fail.
        Assert.Null(info!.Token);
    }
}
