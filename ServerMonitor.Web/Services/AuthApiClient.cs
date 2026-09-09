using System.Net;
using ServerMonitor.Web.Models;

namespace ServerMonitor.Web.Services;

/// <summary>
/// Calls into the account section of the API. This client holds no session — that lives in
/// the web app's cookie; here there is only the question of whether a username and password
/// match, plus account management.
/// </summary>
public class AuthApiClient
{
    private readonly HttpClient _httpClient;

    public AuthApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Whether any account exists yet. Until one does, the system needs setting up.</summary>
    public async Task<bool> HasUsersAsync(CancellationToken cancellationToken = default)
    {
        var state = await _httpClient.GetFromJsonAsync<AuthState>("api/auth/state", cancellationToken);

        return state?.HasUsers ?? false;
    }

    /// <summary>The name of whoever signed in, and the stamp that keeps their session alive.</summary>
    /// <remarks>Null on refusal; the reason for it stays inside the API.</remarks>
    public async Task<SignedInUser?> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/auth/login",
            new { username, password },
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);

        return result is null ? null : new SignedInUser(result.Username, result.SecurityStamp);
    }

    /// <summary>
    /// Whether a session started earlier is still valid.
    /// </summary>
    /// <remarks>
    /// A network failure is answered with true on purpose. This runs on ordinary page requests,
    /// and treating an unreachable API as "signed out" would log everyone out the moment the API
    /// restarted — turning a brief outage into a fleet-wide sign-out. The check runs again in a
    /// minute; a genuinely revoked session survives that much longer and no more.
    /// </remarks>
    public async Task<bool> IsSessionValidAsync(
        string username,
        string securityStamp,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "api/auth/validate",
                new { username, securityStamp },
                cancellationToken);

            return response.StatusCode != HttpStatusCode.Unauthorized;
        }
        catch (HttpRequestException)
        {
            return true;
        }
        catch (TaskCanceledException)
        {
            return true;
        }
    }

    /// <summary>Creates the first account. Returns an error message, or null on success.</summary>
    public async Task<string?> SetupAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/auth/setup",
            new { username, password },
            cancellationToken);

        return response.IsSuccessStatusCode
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<List<AccountSummary>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await _httpClient.GetFromJsonAsync<List<AccountSummary>>("api/auth/users", cancellationToken);

        return users ?? new List<AccountSummary>();
    }

    public async Task<string?> CreateUserAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/auth/users",
            new { username, password },
            cancellationToken);

        return response.IsSuccessStatusCode
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string?> DeleteUserAsync(int id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"api/auth/users/{id}", cancellationToken);

        return response.IsSuccessStatusCode
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private sealed class AuthState
    {
        public bool HasUsers { get; set; }
    }

    private sealed class LoginResponse
    {
        public string Username { get; set; } = string.Empty;
        public string SecurityStamp { get; set; } = string.Empty;
    }
}
