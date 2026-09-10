using System.Net;
using System.Net.Http.Json;
using ServerMonitor.Web.Models;

namespace ServerMonitor.Web.Services;

public class MetricsApiClient
{
    private readonly HttpClient _httpClient;

    public MetricsApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>The fleet with its latest values — what the home page is drawn from.</summary>
    public async Task<List<ServerSummary>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        var servers = await _httpClient.GetFromJsonAsync<List<ServerSummary>>(
            "api/servers", cancellationToken);

        return servers ?? new List<ServerSummary>();
    }

    /// <summary>A single machine. null means there is no such machine — deleted in another tab, say.</summary>
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

    /// <summary>Deletes a machine together with its entire history. This cannot be undone.</summary>
    /// <summary>
    /// Sets a machine's own silence threshold, or returns it to the fleet default with null.
    /// Returns the reason for a refusal, or null on success.
    /// </summary>
    public async Task<string?> SetOfflineThresholdAsync(
        Guid serverId,
        int? seconds,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"api/servers/{serverId}/offline-threshold", new { seconds }, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var reason = await response.Content.ReadAsStringAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(reason) ? "The threshold could not be saved." : reason;
    }

    /// <summary>Renames a machine. Returns the reason for a refusal, or null on success.</summary>
    public async Task<string?> RenameServerAsync(
        Guid serverId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync(
            $"api/servers/{serverId}", new { name }, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var reason = await response.Content.ReadAsStringAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(reason) ? "The machine could not be renamed." : reason;
    }

    public async Task<bool> DeleteServerAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"api/servers/{serverId}", cancellationToken);

        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// The machine's latest reading. Returns null when there are none yet: for an agent that
    /// has only just registered this is a normal state rather than an error.
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

    public async Task<List<MetricTrendItem>> GetTrendAsync(
        Guid serverId,
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        var items = await _httpClient.GetFromJsonAsync<List<MetricTrendItem>>(
            $"api/servers/{serverId}/metrics/trend?days={days}", cancellationToken);

        return items ?? new List<MetricTrendItem>();
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
    /// Alerts. Without a serverId they cover the whole fleet: the question "where did
    /// something break" is about every machine at once.
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
