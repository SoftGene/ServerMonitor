using System.Net;

namespace ServerMonitor.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ServiceKeyTests
{
    private readonly ApiFixture _api;

    public ServiceKeyTests(ApiFixture api)
    {
        _api = api;
    }

    [Theory]
    [InlineData("/api/servers")]
    [InlineData("/api/alerts")]
    [InlineData("/api/settings")]
    [InlineData("/api/auth/state")]
    public async Task ReadEndpoints_RefuseARequestWithNoServiceKey(string path)
    {
        using var client = _api.CreateAnonymousClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReadEndpoints_RefuseAWrongServiceKey()
    {
        using var client = _api.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", "not-the-key");

        var response = await client.GetAsync("/api/servers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/servers")]
    [InlineData("/api/alerts")]
    [InlineData("/api/settings")]
    [InlineData("/api/auth/state")]
    public async Task ReadEndpoints_AnswerWithTheServiceKey(string path)
    {
        using var client = _api.CreateServiceClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_NeedsNoKeyAtAll()
    {
        // An uptime service cannot present a secret, so demanding one would make the check
        // useless to the only thing that is going to call it.
        using var client = _api.CreateAnonymousClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", await response.Content.ReadAsStringAsync());
    }
}
