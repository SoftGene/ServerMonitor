using System.Net.Http.Json;
using System.Runtime.InteropServices;

namespace ServerMonitor.Agent;

/// <summary>Обёртка над двумя HTTP-вызовами к серверу мониторинга: регистрация и отправка.</summary>
public class AgentClient
{
    private readonly HttpClient _httpClient;

    public AgentClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Первый запуск: обменять общий токен установки на персональный ключ.</summary>
    public async Task<AgentState> RegisterAsync(string enrollmentToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agents/register")
        {
            Content = JsonContent.Create(new
            {
                hostname = Environment.MachineName,
                operatingSystem = RuntimeInformation.OSDescription,
                agentVersion = AgentVersion
            })
        };

        request.Headers.Add("X-Enrollment-Token", enrollmentToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var state = await response.Content.ReadFromJsonAsync<AgentState>(cancellationToken);

        return state ?? throw new InvalidOperationException("Registration response was empty.");
    }

    /// <summary>Отправить пачку замеров. Пачкой — потому что после разрыва связи их накапливается много.</summary>
    public async Task SendAsync(
        string apiKey,
        IReadOnlyList<MetricReport> readings,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/ingest")
        {
            Content = JsonContent.Create(readings)
        };

        request.Headers.Add("X-Api-Key", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static string AgentVersion =>
        typeof(AgentClient).Assembly.GetName().Version?.ToString() ?? "unknown";
}
