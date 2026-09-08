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

    /// <summary>Returns the name of whoever signed in, or null. The reason for a refusal stays inside.</summary>
    public async Task<string?> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
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

        return result?.Username;
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
    }
}
