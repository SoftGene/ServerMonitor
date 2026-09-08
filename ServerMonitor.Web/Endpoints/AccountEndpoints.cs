using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ServerMonitor.Web.Services;

namespace ServerMonitor.Web.Endpoints;

/// <summary>
/// Sign-in and sign-out as plain HTTP requests rather than through Blazor components.
/// </summary>
/// <remarks>
/// The reason is structural rather than stylistic. The app runs in InteractiveServer mode, so
/// its pages live inside a SignalR connection. A cookie, however, is set by an <b>HTTP response
/// header</b>, and that response was sent long ago — back when the page was loading. A button
/// handler in an interactive component physically cannot sign anyone in: calling SignInAsync
/// there ends in an exception about headers that have already been sent.
///
/// So sign-in is returned to an ordinary request/response round trip: the form posts here,
/// there is a live HttpContext, and the cookie goes out in the response header.
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

            // The form is read by hand, so the middleware's automatic check does not apply
            // here — it is invoked explicitly. Without it another site could sign a visitor
            // into an account of its choosing (login CSRF).
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

            // The account exists now, so sign them straight in rather than asking twice in a row.
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
    /// Allows only site-relative addresses.
    /// </summary>
    /// <remarks>
    /// Without this check the returnUrl parameter becomes an <b>open redirect</b>: a link
    /// such as /login?returnUrl=https://evil.example looks like a link to our site but leads
    /// somewhere else once the visitor has signed in. A classic phishing technique.
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
