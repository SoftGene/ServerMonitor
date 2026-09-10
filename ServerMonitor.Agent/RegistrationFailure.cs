using System.Net;

namespace ServerMonitor.Agent;

/// <summary>
/// Tells a registration failure that will pass on its own from one that no retry can fix.
/// </summary>
/// <remarks>
/// The agent used to treat both the same way: log "Registration failed" and do nothing further,
/// while the process stayed alive. Under systemd that reads as a healthy service — active, not
/// restarting — belonging to a machine that simply never appears in the fleet.
/// <para>
/// The two kinds need opposite handling. A server that is unreachable, restarting or rate limiting
/// is the normal state of affairs at boot, when the agent easily starts before the server does, and
/// the right response is to wait and try again. A token the server refuses will be refused on every
/// attempt, and the right response is to stop loudly so someone looks.
/// </para>
/// </remarks>
public static class RegistrationFailure
{
    /// <summary>True when retrying the same request cannot succeed.</summary>
    /// <remarks>
    /// Deliberately narrow: only an explicit refusal of the credentials. Everything else — no
    /// answer at all, a timeout, a 429, any 5xx including the 503 a server returns when it has no
    /// enrollment token configured — is something that can change without anyone touching this
    /// machine, so it is retried.
    /// </remarks>
    public static bool IsPermanent(Exception exception) =>
        exception is HttpRequestException
        {
            StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        };
}
