using System.Net;
using System.Net.Http.Json;
using ServerMonitor.Infrastructure.Auth;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// What makes a signed cookie revocable.
/// </summary>
/// <remarks>
/// A cookie is trusted because it is signed, which is why cookie authentication needs no session
/// store — and why, without something like this, the server has no way to change its mind. Deleting
/// an account did nothing to a browser already holding its cookie; it stayed signed in for the
/// full week the cookie lasts.
/// </remarks>
[Collection(ApiCollection.Name)]
public class SessionRevocationTests : IAsyncLifetime
{
    private readonly ApiFixture _api;

    public SessionRevocationTests(ApiFixture api)
    {
        _api = api;
    }

    public Task InitializeAsync() => _api.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record LoginResponse(string Username, string SecurityStamp);
    private sealed record UserRow(int Id, string Username);

    private const string Password = "long-enough-password";

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = Password });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static async Task<HttpStatusCode> ValidateAsync(HttpClient client, string username, string stamp)
    {
        var response = await client.PostAsJsonAsync("/api/auth/validate",
            new { username, securityStamp = stamp });

        return response.StatusCode;
    }

    [Fact]
    public async Task Login_HandsOutAStampTheSessionCanBeCheckedAgainst()
    {
        using var client = _api.CreateServiceClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "owner", password = Password });

        var session = await LoginAsync(client, "owner");

        Assert.NotEmpty(session.SecurityStamp);
        Assert.Equal(HttpStatusCode.OK, await ValidateAsync(client, "owner", session.SecurityStamp));
    }

    [Fact]
    public async Task DeletingAnAccount_EndsItsExistingSession()
    {
        using var client = _api.CreateServiceClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "owner", password = Password });

        var created = await client.PostAsJsonAsync("/api/auth/users",
            new { username = "leaver", password = Password });
        var leaver = await created.Content.ReadFromJsonAsync<UserRow>();

        var session = await LoginAsync(client, "leaver");
        Assert.Equal(HttpStatusCode.OK, await ValidateAsync(client, "leaver", session.SecurityStamp));

        var deleted = await client.DeleteAsync($"/api/auth/users/{leaver!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // This is the whole point of the feature. Before it, the browser holding that cookie
        // carried on working for a week.
        Assert.Equal(HttpStatusCode.Unauthorized, await ValidateAsync(client, "leaver", session.SecurityStamp));
    }

    [Fact]
    public async Task ChangingThePassword_EndsExistingSessions()
    {
        using var client = _api.CreateServiceClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "owner", password = Password });

        var session = await LoginAsync(client, "owner");

        // Changing a password has no HTTP endpoint on purpose — it is the reset-password console
        // command, which runs on the server and never puts the new password on the wire. The
        // service behind it is what this exercises.
        var changed = await _api.WithServiceAsync<UserService, bool>(users =>
            users.SetPasswordAsync("owner", "a-different-long-password", default));

        Assert.True(changed);

        // Someone changing a password because it may have leaked expects the intruder to be signed
        // out. A reset that leaves them in is worse than none: it looks like the problem was dealt
        // with.
        Assert.Equal(HttpStatusCode.Unauthorized, await ValidateAsync(client, "owner", session.SecurityStamp));
    }

    [Fact]
    public async Task ANewSessionAfterAPasswordChange_IsAccepted()
    {
        using var client = _api.CreateServiceClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "owner", password = Password });
        await LoginAsync(client, "owner");

        await _api.WithServiceAsync<UserService, bool>(users =>
            users.SetPasswordAsync("owner", "a-different-long-password", default));

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "owner", password = "a-different-long-password" });

        response.EnsureSuccessStatusCode();

        var session = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        // Revoking the old sessions must not lock out the new one.
        Assert.Equal(HttpStatusCode.OK, await ValidateAsync(client, "owner", session.SecurityStamp));
    }

    [Fact]
    public async Task AStampFromOneAccount_DoesNotValidateAnother()
    {
        using var client = _api.CreateServiceClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "owner", password = Password });
        await client.PostAsJsonAsync("/api/auth/users", new { username = "other", password = Password });

        var owner = await LoginAsync(client, "owner");

        Assert.Equal(HttpStatusCode.Unauthorized, await ValidateAsync(client, "other", owner.SecurityStamp));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-stamp")]
    public async Task AnEmptyOrWrongStamp_IsRefused(string stamp)
    {
        using var client = _api.CreateServiceClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "owner", password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, await ValidateAsync(client, "owner", stamp));
    }

    [Fact]
    public async Task AnUnknownAccount_IsRefused()
    {
        using var client = _api.CreateServiceClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await ValidateAsync(client, "nobody", "any-stamp"));
    }

    [Fact]
    public async Task Validation_StillNeedsTheServiceKey()
    {
        using var client = _api.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/validate",
            new { username = "owner", securityStamp = "any" });

        // The stamp is not a credential and grants nothing on its own — but only because every
        // call that accepts it is already behind the service key.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
