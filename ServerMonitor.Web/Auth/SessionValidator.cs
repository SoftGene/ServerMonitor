using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using ServerMonitor.Web.Services;

namespace ServerMonitor.Web.Auth;

/// <summary>
/// Re-checks, on an interval, that a session which started earlier should still be honoured.
/// </summary>
/// <remarks>
/// A cookie is believed because it is signed, and that is the whole reason cookie authentication
/// needs no server-side session store. The price is that the server has no way to change its mind:
/// delete an account and the browser holding its cookie stays signed in until the cookie expires,
/// which here is a week.
/// <para>
/// So the cookie carries the account's security stamp, and this asks the API whether that stamp is
/// still the current one. When it is not — the account was deleted, or its password was changed —
/// the principal is rejected and the cookie is deleted.
/// </para>
/// </remarks>
public static class SessionValidator
{
    /// <summary>
    /// How long a session is trusted between checks.
    /// </summary>
    /// <remarks>
    /// The trade is plain: checking on every request would cost an API call per page and make the
    /// dashboard's own polling expensive, while checking rarely leaves a revoked session working
    /// for longer. A minute keeps the window short enough to be worth having and the traffic low
    /// enough to ignore.
    /// </remarks>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private const string LastCheckedKey = "stamp.checked";

    public static CookieAuthenticationEvents Build() => new()
    {
        OnValidatePrincipal = ValidateAsync
    };

    private static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var username = context.Principal?.Identity?.Name;
        var stamp = context.Principal?.FindFirst(SessionClaims.SecurityStamp)?.Value;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(stamp))
        {
            // A cookie issued before sessions carried a stamp. There is no way to tell whether it
            // is still legitimate, and the safe reading of "cannot tell" is to end it: the person
            // signs in again once, and every session after that is revocable.
            await RejectAsync(context);

            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;

        if (context.Properties.Items.TryGetValue(LastCheckedKey, out var lastChecked)
            && DateTimeOffset.TryParse(lastChecked, out var lastCheckedUtc)
            && nowUtc - lastCheckedUtc < CheckInterval)
        {
            return;
        }

        var auth = context.HttpContext.RequestServices.GetRequiredService<AuthApiClient>();

        if (!await auth.IsSessionValidAsync(username, stamp, context.HttpContext.RequestAborted))
        {
            await RejectAsync(context);

            return;
        }

        context.Properties.Items[LastCheckedKey] = nowUtc.ToString("O");

        // Without this the new timestamp is never written back and the check runs on every single
        // request — the interval above would silently do nothing at all.
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();

        // Rejecting alone leaves the cookie in the browser, so the same dead session would be
        // presented and rejected on every request afterwards. Signing out deletes it.
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
