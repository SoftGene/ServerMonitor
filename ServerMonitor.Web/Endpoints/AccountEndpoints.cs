using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ServerMonitor.Web.Services;

namespace ServerMonitor.Web.Endpoints;

/// <summary>
/// Вход и выход — обычными HTTP-запросами, а не через компоненты Blazor.
/// </summary>
/// <remarks>
/// Причина не в стиле, а в устройстве. Приложение работает в режиме InteractiveServer, то есть
/// страницы живут внутри соединения SignalR. Cookie же выставляется <b>заголовком HTTP-ответа</b>,
/// а он давно отправлен — ещё когда страница загружалась. Обработчик кнопки в интерактивном
/// компоненте физически не может залогинить пользователя: вызов SignInAsync там завершится
/// исключением про уже отправленные заголовки.
///
/// Поэтому вход возвращён в обычный цикл «запрос — ответ»: форма шлёт POST сюда, здесь есть
/// живой HttpContext, и cookie уходит в заголовке ответа.
/// </remarks>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost("/account/login", async (
            HttpContext context,
            IAntiforgery antiforgery,
            AuthApiClient authClient,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Account");

            // Форму читаем вручную, поэтому автоматическая проверка middleware сюда не
            // распространяется — вызываем её явно. Без неё чужой сайт мог бы залогинить
            // посетителя в подставную учётку (login CSRF).
            if (!await IsRequestValidAsync(antiforgery, context, logger))
            {
                return Results.Redirect("/login?error=1");
            }

            var form = await context.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var returnUrl = SafeReturnUrl(form["returnUrl"].ToString());

            var loggedInAs = await authClient.LoginAsync(username, password);

            if (loggedInAs is null)
            {
                logger.LogWarning("Failed sign-in attempt for {Username}.", username);

                return Results.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, loggedInAs)],
                CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            logger.LogInformation("{Username} signed in.", loggedInAs);

            return Results.Redirect(returnUrl);
        });

        app.MapPost("/account/setup", async (
            HttpContext context,
            IAntiforgery antiforgery,
            AuthApiClient authClient,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Account");

            if (!await IsRequestValidAsync(antiforgery, context, logger))
            {
                return Results.Redirect("/setup?error=Request+could+not+be+verified.");
            }

            var form = await context.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var confirm = form["confirm"].ToString();

            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                return Results.Redirect("/setup?error=Passwords+do+not+match.");
            }

            var problem = await authClient.SetupAsync(username, password);

            if (problem is not null)
            {
                return Results.Redirect($"/setup?error={Uri.EscapeDataString(problem)}");
            }

            // Учётка создана — сразу впускаем, чтобы не заставлять входить второй раз подряд.
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, username)],
                CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            logger.LogInformation("First account {Username} created; setup complete.", username);

            return Results.Redirect("/");
        });

        app.MapPost("/account/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return Results.Redirect("/login");
        });
    }

    private static async Task<bool> IsRequestValidAsync(
        IAntiforgery antiforgery,
        HttpContext context,
        ILogger logger)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);

            return true;
        }
        catch (AntiforgeryValidationException ex)
        {
            logger.LogWarning(ex, "Rejected a form post that failed antiforgery validation.");

            return false;
        }
    }

    /// <summary>
    /// Разрешает только относительные адреса внутри сайта.
    /// </summary>
    /// <remarks>
    /// Без этой проверки параметр returnUrl превращается в <b>открытое перенаправление</b>
    /// (open redirect): ссылка вида /login?returnUrl=https://зло.example выглядит ссылкой на
    /// наш сайт, но после входа уводит на чужой. Классический приём в фишинге.
    /// </remarks>
    private static string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) ||
            !returnUrl.StartsWith('/') ||
            returnUrl.StartsWith("//") ||
            returnUrl.StartsWith("/\\"))
        {
            return "/";
        }

        return returnUrl;
    }
}
