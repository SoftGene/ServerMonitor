using ServerMonitor.Agent;
using ServerMonitor.Collection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection(AgentOptions.SectionName));

var serverUrl = builder.Configuration["Agent:ServerUrl"];

// Fail fast: without a server address the agent is useless, and saying so up front is kinder.
if (string.IsNullOrWhiteSpace(serverUrl))
{
    throw new InvalidOperationException(
        "Agent:ServerUrl is not configured. Set it in appsettings.json " +
        "or via the Agent__ServerUrl environment variable.");
}

builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
builder.Services.AddHttpClient<AgentClient>(client => client.BaseAddress = new Uri(serverUrl));
builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
host.Run();
