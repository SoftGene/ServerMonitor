using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ServerMonitor.Api.Commands;
using ServerMonitor.Infrastructure.Alerting;
using ServerMonitor.Infrastructure.Auth;
using ServerMonitor.Infrastructure.Data;
using ServerMonitor.Infrastructure.Monitoring;
using ServerMonitor.Infrastructure.Telegram;

// Parse the command BEFORE creating the builder: that one feeds args to the configuration
// provider, which throws a FormatException on a bare word like "reset-password".
if (args is ["reset-password", var accountName])
{
    return await ResetPasswordCommand.RunAsync(accountName);
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured. " +
        "Set it with 'dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"...\"' for local development, " +
        "or via the ConnectionStrings__DefaultConnection environment variable on a server.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.Configure<MonitoringOptions>(
    builder.Configuration.GetSection(MonitoringOptions.SectionName));

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// The API no longer collects metrics: agents send them via POST api/ingest.

// Accounts. The throttle is a singleton: the counters of failed attempts are shared across
// the application, otherwise every request would start counting afresh and there would be no
// protection at all.
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddScoped<UserService>();

// Delivery channels. The log channel is always present — it guarantees an event is visible
// somewhere even with no Telegram configured.
builder.Services.AddSingleton<IAlertChannel, LogAlertChannel>();
builder.Services.AddSingleton<IAlertChannel, TelegramAlertChannel>();

// Rule checking depends on no channel: an unconfigured bot used to switch it off entirely.
builder.Services.AddHostedService<AlertingService>();
builder.Services.AddHostedService<TelegramBotService>();

var app = builder.Build();

// Apply migrations at startup. Without this the promise of a single `docker compose up` breaks:
// the API would come up against an empty database.
//
// This is right for a single instance and wrong for several: on a simultaneous start they would
// race each other. A multi-instance deployment runs migrations as a separate step instead.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    startupLogger.LogInformation("Applying database migrations...");
    await dbContext.Database.MigrateAsync();
    startupLogger.LogInformation("Database is up to date.");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

// An external watcher needs somewhere to poll: the system cannot report its own death
// (see guide chapter 11). Deliberately unauthenticated — a health check that demands a secret
// is one an uptime service cannot perform — and deliberately terse, because what exactly broke
// is nobody else's business.
app.MapGet("/healthz", async (AppDbContext dbContext) =>
    await dbContext.Database.CanConnectAsync()
        ? Results.Text("healthy")
        : Results.Text("unhealthy", statusCode: StatusCodes.Status503ServiceUnavailable));

app.MapControllers();

app.Run();

return 0;
