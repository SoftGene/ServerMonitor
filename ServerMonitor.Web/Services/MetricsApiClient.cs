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
}