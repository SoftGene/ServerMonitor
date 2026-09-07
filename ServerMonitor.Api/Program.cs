using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ServerMonitor.Infrastructure.Data;
using ServerMonitor.Infrastructure.Monitoring;
using ServerMonitor.Infrastructure.Telegram;

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

// Метрики API больше не снимает: их присылают агенты через POST api/ingest.
// Здесь остаётся только слежение за порогами и оповещения.
builder.Services.AddHostedService<TelegramBotService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
