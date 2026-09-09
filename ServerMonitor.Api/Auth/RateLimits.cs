using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ServerMonitor.Api.Auth;

/// <summary>
/// Limits on how fast the endpoints that accept a shared secret can be tried.
/// </summary>
/// <remarks>
/// Agent registration and login both take a secret from an unauthenticated caller, which means
/// both can be guessed at. The login endpoint already counts failures per username, but nothing
/// stopped anyone from trying enrollment tokens as fast as the network allowed — and the
/// enrollment token is one shared value that never expires, so an unlimited number of attempts is
/// the whole attack.
/// <para>
/// This is a blunt instrument and that is the right shape for it: it does not need to distinguish
/// a real agent from an attacker, only to make the difference between a thousand attempts a second
/// and a few a minute. A real agent registers once in its life.
/// </para>
/// </remarks>
public static class RateLimits
{
    /// <summary>Applied to agent registration.</summary>
    public const string Enrollment = "enrollment";

    /// <summary>Applied to the endpoints that check a person's credentials.</summary>
    public const string Credentials = "credentials";

    /// <summary>Defaults, overridable from the "RateLimits" configuration section.</summary>
    private const int DefaultEnrollmentPerHour = 10;
    private const int DefaultCredentialsPerMinute = 20;

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Read once here rather than per request. Configurable mainly so the tests can pick
        // limits low enough to reach without waiting an hour, and so an operator setting up a
        // large fleet in one afternoon is not fighting the enrollment limit.
        var section = configuration.GetSection("RateLimits");
        var enrollmentPerHour = section.GetValue("EnrollmentPerHour", DefaultEnrollmentPerHour);
        var credentialsPerMinute = section.GetValue("CredentialsPerMinute", DefaultCredentialsPerMinute);

        services.AddRateLimiter(options =>
        {
            // 429 rather than the default 503: the caller should be told to slow down, not that
            // the service is unavailable — one invites a retry in a moment, the other invites a
            // failover.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partitioned by remote address. Imperfect on purpose: everyone behind one NAT shares
            // a bucket, and an attacker with many addresses gets many buckets. It still turns an
            // unbounded guessing rate into a bounded one, which is the entire goal — the login
            // throttle already covers the case this misses, by counting per username instead.
            options.AddPolicy(Enrollment, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    // A machine registers once. Ten an hour is generous for setting up a fleet and
                    // useless for working through a keyspace.
                    PermitLimit = Math.Max(1, enrollmentPerHour),
                    Window = TimeSpan.FromHours(1)
                }));

            options.AddPolicy(Credentials, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    // Enough that a person mistyping a password never notices it, and far below
                    // what an automated attempt needs.
                    PermitLimit = Math.Max(1, credentialsPerMinute),
                    Window = TimeSpan.FromMinutes(1)
                }));
        });

        return services;
    }
}
