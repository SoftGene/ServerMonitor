using ServerMonitor.Web.Models;

namespace ServerMonitor.Web.Services;

public class MetricsApiClient
{
    private readonly HttpClient _httpClient;

    public MetricsApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ServerStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<ServerStatus>(
            "api/metrics/status", cancellationToken);
    }

    public async Task<List<MetricHistoryItem>> GetHistoryAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        var items = await _httpClient.GetFromJsonAsync<List<MetricHistoryItem>>(
            $"api/metrics/history?count={count}", cancellationToken);

        return items ?? new List<MetricHistoryItem>();
    }

    public async Task<PagedResult<MetricHistoryItem>> GetHistoryPagedAsync(
        int page = 1,
        int pageSize = 20,
        string sortBy = "timestamp",
        string sortDir = "desc",
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/metrics/history/paged?page={page}&pageSize={pageSize}&sortBy={sortBy}&sortDir={sortDir}";

        if (from.HasValue)
            url += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue)
            url += $"&to={to.Value:yyyy-MM-dd}";

        var result = await _httpClient.GetFromJsonAsync<PagedResult<MetricHistoryItem>>(url, cancellationToken);
        return result ?? new PagedResult<MetricHistoryItem>();
    }
}