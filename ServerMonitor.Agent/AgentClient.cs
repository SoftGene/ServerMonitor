using System.Net.Http.Json;
using System.Runtime.InteropServices;

namespace ServerMonitor.Agent;

/// <summary>A wrapper over the two HTTP calls to the monitoring server: register and send.</summary>
public class AgentClient
{
    private readonly HttpClient _httpClient;

    public AgentClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>First run: exchange the shared enrollment token for a personal key.</summary>
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

    /// <summary>Sends a batch of readings. A batch, because a network outage leaves many of them.</summary>
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
