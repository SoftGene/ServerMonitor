using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ServerMonitor.Api.Commands;
using ServerMonitor.Infrastructure.Alerting;
using ServerMonitor.Infrastructure.Auth;
using ServerMonitor.Infrastructure.Data;
using ServerMonitor.Infrastructure.Monitoring;
using ServerMonitor.Infrastructure.Telegram;

// Разбираем команду ДО создания builder: тот скармливает args провайдеру конфигурации,
// который на голом слове «reset-password» падает с FormatException.
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

// Метрики API больше не снимает: их присылают агенты через POST api/ingest.

// Учётные записи. Троттлинг — singleton: счётчики промахов общие на всё приложение,
// иначе каждый запрос начинал бы считать заново и защиты не было бы вовсе.
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddScoped<UserService>();

// Каналы доставки. Журнальный нужен всегда — он гарантирует, что событие где-то видно
// даже без настроенного Telegram.
builder.Services.AddSingleton<IAlertChannel, LogAlertChannel>();
builder.Services.AddSingleton<IAlertChannel, TelegramAlertChannel>();

// Проверка правил не зависит ни от одного канала: раньше ненастроенный бот выключал её целиком.
builder.Services.AddHostedService<AlertingService>();
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

return 0;
