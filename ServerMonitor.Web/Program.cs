using Microsoft.AspNetCore.Authentication.Cookies;
using ServerMonitor.Web.Components;
using ServerMonitor.Web.Endpoints;
using ServerMonitor.Web.Services;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var baseUrl = builder.Configuration["ApiSettings:BaseUrl"]
    ?? throw new InvalidOperationException("ApiSettings:BaseUrl is not configured.");

// Служебный ключ. Без него API не ответит ни на один запрос чтения, поэтому проверяем
// настройку при старте: лучше не запуститься с внятным сообщением, чем показывать
// пользователю 401 на каждой странице.
var serviceKey = builder.Configuration["ApiSettings:ServiceKey"];

if (string.IsNullOrWhiteSpace(serviceKey))
{
    throw new InvalidOperationException(
        "ApiSettings:ServiceKey is not configured. Set it with 'dotnet user-secrets set " +
        "\"ApiSettings:ServiceKey\" \"...\"' for local development, or via the " +
        "ApiSettings__ServiceKey environment variable on a server. It must match Api:ServiceKey " +
        "on the API side.");
}

builder.Services.AddHttpClient<MetricsApiClient>(client =>
{
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("X-Service-Key", serviceKey);
});

builder.Services.AddHttpClient<AuthApiClient>(client =>
{
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("X-Service-Key", serviceKey);
});

// Сессия человека живёт в cookie. Blazor Server и так держит серверное состояние, так что
// токен в браузере ничего бы не упростил, зато добавил бы хранение и обновление.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRadzenComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Порядок обязателен: сначала выяснить, кто пришёл, потом решить, пускать ли, и только
// затем отдавать страницы.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapAccountEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
