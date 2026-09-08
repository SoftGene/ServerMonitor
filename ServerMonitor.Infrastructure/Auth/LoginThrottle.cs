namespace ServerMonitor.Infrastructure.Auth;

/// <summary>
/// A counter of failed sign-ins. After several misses in a row the name is blocked for a while.
/// </summary>
/// <remarks>
/// Counted per <b>username</b> rather than per IP address. A whole house or office can sit
/// behind one address, so blocking by it would deny service to bystanders. The cost of the
/// choice is stated rather than hidden: someone who knows the login can lock the owner out
/// for a while. That is an acceptable price for a home system; a public service would need
/// a name-and-address pair plus a captcha.
///
/// Time arrives as an argument — the same trick as in HeartbeatRule: otherwise the expiry
/// behaviour could not be tested without sleeping for five minutes.
///
/// The state lives in memory. Counters reset on restart, and that is deliberate: keeping
/// them in the database would mean a write per failed attempt — a convenient way to make
/// someone else's disk do the work.
/// </remarks>
public class LoginThrottle
{
    private sealed class Attempts
    {
        public int Failures { get; set; }
        public DateTime? BlockedUntilUtc { get; set; }
    }

    private readonly Dictionary<string, Attempts> _attempts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _sync = new();
    private readonly int _maxAttempts;
    private readonly TimeSpan _blockFor;

    public LoginThrottle(int maxAttempts = 5, TimeSpan? blockFor = null)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _blockFor = blockFor ?? TimeSpan.FromMinutes(5);
    }

    public bool IsBlocked(string username, DateTime nowUtc)
    {
        lock (_sync)
        {
            if (!_attempts.TryGetValue(username, out var state) || state.BlockedUntilUtc is null)
            {
                return false;
            }

            if (state.BlockedUntilUtc > nowUtc)
            {
                return true;
            }

            // The block has expired: clear it and start counting again, otherwise the very
            // next failure would lock the account straight back out.
            _attempts.Remove(username);

            return false;
        }
    }

    public void RecordFailure(string username, DateTime nowUtc)
    {
        lock (_sync)
        {
            if (_attempts.TryGetValue(username, out var existing) &&
                existing.BlockedUntilUtc is { } until && until <= nowUtc)
            {
                _attempts.Remove(username);
            }

            if (!_attempts.TryGetValue(username, out var state))
            {
                state = new Attempts();
                _attempts[username] = state;
            }

            state.Failures++;

            if (state.Failures >= _maxAttempts)
            {
                state.BlockedUntilUtc = nowUtc + _blockFor;
            }
        }
    }

    /// <summary>A successful sign-in wipes the history of misses.</summary>
    public void Reset(string username)
    {
        lock (_sync)
        {
            _attempts.Remove(username);
        }
    }
}
