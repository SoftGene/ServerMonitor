using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ServerMonitor.Infrastructure.Data;
using ServerMonitor.Infrastructure.Monitoring;
using ServerMonitor.Infrastructure.Telegram;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();

builder.Services.AddHostedService<MetricsCollectorService>();

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
