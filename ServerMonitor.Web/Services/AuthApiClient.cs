using System.Net;
using ServerMonitor.Web.Models;

namespace ServerMonitor.Web.Services;

/// <summary>
/// Обращения к разделу учётных записей API. Сессию этот клиент не держит — она живёт в cookie
/// веб-приложения; здесь только вопрос «пара логин-пароль верна?» и управление учётками.
/// </summary>
public class AuthApiClient
{
    private readonly HttpClient _httpClient;

    public AuthApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Заведена ли хоть одна учётка. Пока нет — систему нужно настроить.</summary>
    public async Task<bool> HasUsersAsync(CancellationToken cancellationToken = default)
    {
        var state = await _httpClient.GetFromJsonAsync<AuthState>("api/auth/state", cancellationToken);

        return state?.HasUsers ?? false;
    }

    /// <summary>Возвращает имя вошедшего или null. Причину отказа наружу не выносим.</summary>
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

    /// <summary>Создаёт первую учётку. Возвращает текст ошибки или null при успехе.</summary>
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
