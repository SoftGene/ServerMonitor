using Microsoft.AspNetCore.Authentication.Cookies;
using ServerMonitor.Web.Auth;
using ServerMonitor.Web.Components;
using ServerMonitor.Web.Endpoints;
using ServerMonitor.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var baseUrl = builder.Configuration["ApiSettings:BaseUrl"]
    ?? throw new InvalidOperationException("ApiSettings:BaseUrl is not configured.");

// The service key. Without it the API answers no read request at all, so the setting is
// checked at startup: failing to start with a clear message beats showing the user a 401 on
// every page.
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

// A person's session lives in a cookie. Blazor Server already keeps state on the server, so
// a token in the browser would simplify nothing while adding storage and refresh to manage.
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

        // A signed cookie is otherwise impossible to revoke: deleting an account would leave the
        // browser holding it signed in for the full week above. This re-checks with the API on an
        // interval and ends the session once the account's stamp no longer matches.
        options.Events = SessionValidator.Build();
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

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

// The order matters: first work out who arrived, then decide whether to let them in, and
// only then serve the pages.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapAccountEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
