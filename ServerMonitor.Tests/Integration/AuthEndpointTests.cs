using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ServerMonitor.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AuthEndpointTests : IAsyncLifetime
{
    private readonly ApiFixture _api;

    public AuthEndpointTests(ApiFixture api)
    {
        _api = api;
    }

    public Task InitializeAsync() => _api.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record AuthState(bool HasUsers);

    [Fact]
    public async Task Setup_CreatesTheFirstAccountAndThenClosesItself()
    {
        using var client = _api.CreateServiceClient();

        var state = await client.GetFromJsonAsync<AuthState>("/api/auth/state");
        Assert.False(state!.HasUsers);

        var first = await client.PostAsJsonAsync("/api/auth/setup",
            new { username = "owner", password = "long-enough-password" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Without this the endpoint would be open registration for an administrator account.
        var second = await client.PostAsJsonAsync("/api/auth/setup",
            new { username = "intruder", password = "long-enough-password" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        state = await client.GetFromJsonAsync<AuthState>("/api/auth/state");
        Assert.True(state!.HasUsers);
    }

    [Fact]
    public async Task Setup_RefusesAShortPassword()
    {
        using var client = _api.CreateServiceClient();

        var response = await client.PostAsJsonAsync("/api/auth/setup",
            new { username = "owner", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_AcceptsTheRightPasswordAndRefusesTheWrongOne()
    {
        using var client = _api.CreateServiceClient();

        await client.PostAsJsonAsync("/api/auth/setup",
            new { username = "owner", password = "long-enough-password" });

        var good = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "owner", password = "long-enough-password" });
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);

        var bad = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "owner", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Login_AnswersIdenticallyForAnUnknownUserAndAWrongPassword()
    {
        using var client = _api.CreateServiceClient();

        await client.PostAsJsonAsync("/api/auth/setup",
            new { username = "owner", password = "long-enough-password" });

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "owner", password = "wrong-password" });

        var unknownUser = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "nobody", password = "wrong-password" });

        // Telling these apart would let an attacker enumerate which logins exist, and knowing
        // that is half the work. The bodies have to match too, not just the status codes.
        Assert.Equal(wrongPassword.StatusCode, unknownUser.StatusCode);
        Assert.Equal(
            await ReadComparableBodyAsync(wrongPassword),
            await ReadComparableBodyAsync(unknownUser));
    }

    /// <summary>The response body with its trace identifier removed.</summary>
    /// <remarks>
    /// ASP.NET stamps a fresh traceId into every ProblemDetails, so two identical refusals are
    /// never byte-identical. It is exempt from the comparison because it is derived from the
    /// request and not from the account: it differs between two calls with the same username
    /// exactly as much as between two calls with different ones, so it tells an attacker nothing.
    /// Every other field still has to match.
    /// </remarks>
    private static async Task<string> ReadComparableBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!.AsObject();

        json.Remove("traceId");

        return json.ToJsonString();
    }

    [Fact]
    public async Task Login_BlocksAfterRepeatedFailures()
    {
        using var client = _api.CreateServiceClient();

        await client.PostAsJsonAsync("/api/auth/setup",
            new { username = "throttled", password = "long-enough-password" });

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync("/api/auth/login",
                new { username = "throttled", password = "wrong-password" });
        }

        // The point of the throttle: even the correct password is refused while the block holds.
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "throttled", password = "long-enough-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LastAccount_CannotBeDeleted()
    {
        using var client = _api.CreateServiceClient();

        var created = await client.PostAsJsonAsync("/api/auth/setup",
            new { username = "only-one", password = "long-enough-password" });
        var user = await created.Content.ReadFromJsonAsync<UserRow>();

        var response = await client.DeleteAsync($"/api/auth/users/{user!.Id}");

        // Otherwise the system locks itself out with nobody left who could sign in and fix it.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateUsername_IsRefused()
    {
        using var client = _api.CreateServiceClient();

        await client.PostAsJsonAsync("/api/auth/setup",
            new { username = "owner", password = "long-enough-password" });

        var response = await client.PostAsJsonAsync("/api/auth/users",
            new { username = "OWNER", password = "another-long-password" });

        // Case-insensitive on purpose: "owner" and "OWNER" are the same person to a human.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record UserRow(int Id, string Username);
}
