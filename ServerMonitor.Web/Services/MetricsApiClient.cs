using System.Net;
using ServerMonitor.Web.Models;

namespace ServerMonitor.Web.Services;

public class MetricsApiClient
{
    private readonly HttpClient _httpClient;

    public MetricsApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Список машин парка со свежими значениями — то, из чего рисуется главная страница.</summary>
    public async Task<List<ServerSummary>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        var servers = await _httpClient.GetFromJsonAsync<List<ServerSummary>>(
            "api/servers", cancellationToken);

        return servers ?? new List<ServerSummary>();
    }

    /// <summary>Одна машина. null — такой машины нет (например, её удалили в другой вкладке).</summary>
    public async Task<ServerSummary?> GetServerAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/servers/{serverId}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ServerSummary>(cancellationToken);
    }

    /// <summary>Удаляет машину вместе со всей её историей. Отменить нельзя.</summary>
    public async Task<bool> DeleteServerAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"api/servers/{serverId}", cancellationToken);

        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Последний замер машины. Возвращает null, если замеров ещё нет: у только что
    /// зарегистрированного агента это нормальное состояние, а не ошибка.
    /// </summary>
    public async Task<ServerStatus?> GetStatusAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/servers/{serverId}/metrics/status", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ServerStatus>(cancellationToken);
    }

    public async Task<List<MetricHistoryItem>> GetHistoryAsync(
        Guid serverId,
        int count = 50,
        CancellationToken cancellationToken = default)
    {
        var items = await _httpClient.GetFromJsonAsync<List<MetricHistoryItem>>(
            $"api/servers/{serverId}/metrics/history?count={count}", cancellationToken);

        return items ?? new List<MetricHistoryItem>();
    }

    public async Task<AppSettings?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<AppSettings>("api/settings", cancellationToken);
    }

    public async Task<bool> UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("api/settings", settings, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<PagedResult<MetricHistoryItem>> GetHistoryPagedAsync(
        Guid serverId,
        int page = 1,
        int pageSize = 20,
        string sortBy = "timestamp",
        string sortDir = "desc",
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/servers/{serverId}/metrics/history/paged" +
                  $"?page={page}&pageSize={pageSize}&sortBy={sortBy}&sortDir={sortDir}";

        if (from.HasValue)
            url += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue)
            url += $"&to={to.Value:yyyy-MM-dd}";

        var result = await _httpClient.GetFromJsonAsync<PagedResult<MetricHistoryItem>>(url, cancellationToken);
        return result ?? new PagedResult<MetricHistoryItem>();
    }

    /// <summary>
    /// Алерты. Без serverId — по всему парку: вопрос «где что-то сломалось» относится
    /// ко всем машинам сразу.
    /// </summary>
    public async Task<List<Alert>> GetAlertsAsync(
        int count = 50,
        Guid? serverId = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/alerts?count={count}";

        if (serverId is not null)
        {
            url += $"&serverId={serverId}";
        }

        var alerts = await _httpClient.GetFromJsonAsync<List<Alert>>(url, cancellationToken);

        return alerts ?? new List<Alert>();
    }
}
