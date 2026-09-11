using ServerMonitor.Agent;
using ServerMonitor.Collection;

// The content root is the folder the executable lives in, not the directory it was started from.
// The default is the current directory, so the agent found its appsettings.json only when started
// from its own folder: run from anywhere else it failed with "ServerUrl is not configured" while the
// file sat right beside it. Every service manager that starts it would have been one more working
// directory to get right.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

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
