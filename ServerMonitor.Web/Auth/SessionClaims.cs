namespace ServerMonitor.Web.Auth;

/// <summary>Claim names this application puts in its own cookie.</summary>
public static class SessionClaims
{
    /// <summary>
    /// The account's security stamp, as it stood when the session started.
    /// </summary>
    /// <remarks>
    /// Not a namespaced URI like the built-in claim types, because nothing outside this
    /// application ever reads this cookie. A short name keeps the cookie smaller, and the cookie
    /// travels on every request.
    /// </remarks>
    public const string SecurityStamp = "stamp";
}
