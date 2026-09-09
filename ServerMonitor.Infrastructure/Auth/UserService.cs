using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServerMonitor.Domain.Entities;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Infrastructure.Auth;

/// <summary>Why a sign-in failed. This reason never leaves the server — it only goes to the log.</summary>
public enum LoginFailure
{
    None,
    UnknownUser,
    WrongPassword,
    TooManyAttempts
}

public record LoginResult(User? User, LoginFailure Failure)
{
    public bool Succeeded => User is not null;
}

public class UserService
{
    /// <summary>
    /// The hash of a password nobody can guess. It exists so that checking a username that
    /// does not exist takes as long as checking one that does — see VerifyAsync.
    /// </summary>
    private static readonly string DummyHash =
        new PasswordService().Hash(Guid.NewGuid().ToString());

    private readonly AppDbContext _dbContext;
    private readonly PasswordService _passwords;
    private readonly LoginThrottle _throttle;
    private readonly ILogger<UserService> _logger;

    public UserService(
        AppDbContext dbContext,
        PasswordService passwords,
        LoginThrottle throttle,
        ILogger<UserService> logger)
    {
        _dbContext = dbContext;
        _passwords = passwords;
        _throttle = throttle;
        _logger = logger;
    }

    public async Task<bool> AnyUsersAsync(CancellationToken cancellationToken) =>
        await _dbContext.Users.AsNoTracking().AnyAsync(cancellationToken);

    public async Task<List<User>> ListAsync(CancellationToken cancellationToken) =>
        await _dbContext.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Verifies a username and password.
    /// </summary>
    /// <remarks>
    /// Two things here are deliberate.
    ///
    /// <b>The answer is identical</b> for "no such user" and "wrong password". Different
    /// answers would let an attacker enumerate which logins exist, and knowing that is half
    /// the work.
    ///
    /// <b>The hash is computed even for a name that does not exist.</b> Otherwise an unknown
    /// login would be rejected instantly while a real one took tens of thousands of PBKDF2
    /// iterations, and that difference in timing would leak exactly what the identical error
    /// message hides.
    /// </remarks>
    public async Task<LoginResult> VerifyAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        if (_throttle.IsBlocked(username, nowUtc))
        {
            _logger.LogWarning("Login for {Username} refused: too many failed attempts.", username);

            return new LoginResult(null, LoginFailure.TooManyAttempts);
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null)
        {
            _passwords.Verify(DummyHash, password);
            _throttle.RecordFailure(username, nowUtc);

            _logger.LogWarning("Login failed for unknown user {Username}.", username);

            return new LoginResult(null, LoginFailure.UnknownUser);
        }

        var verification = _passwords.Verify(user.PasswordHash, password);

        if (verification == PasswordVerification.Failed)
        {
            _throttle.RecordFailure(username, nowUtc);

            _logger.LogWarning("Login failed for {Username}: wrong password.", username);

            return new LoginResult(null, LoginFailure.WrongPassword);
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            // The password is right but the hash uses outdated parameters. Rewrite it
            // quietly: the user notices nothing and the strength goes up.
            user.PasswordHash = _passwords.Hash(password);

            // Deliberately no new security stamp here. The stored hash changed, the password did
            // not, and rolling the stamp would end every session of the person who just proved
            // they know it — including the one being created by this very request.
            _logger.LogInformation("Password hash for {Username} upgraded to current parameters.", username);
        }

        user.LastLoginUtc = nowUtc;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _throttle.Reset(username);

        return new LoginResult(user, LoginFailure.None);
    }

    /// <summary>
    /// Whether a session that presented this stamp should still be honoured.
    /// </summary>
    /// <remarks>
    /// A deleted account has no row and fails here, which is the case this was written for. The
    /// comparison is fixed-time out of habit rather than necessity: the stamp is not a credential,
    /// but comparing opaque values with == is the habit worth having, and the one place it is
    /// skipped is the place it turns out to have mattered.
    /// </remarks>
    public async Task<bool> IsSessionValidAsync(
        string username,
        string securityStamp,
        CancellationToken cancellationToken)
    {
        var stored = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Username == username)
            .Select(u => u.SecurityStamp)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored),
            Encoding.UTF8.GetBytes(securityStamp));
    }

    public async Task<User> CreateAsync(string username, string password, CancellationToken cancellationToken)
    {
        var user = new User
        {
            Username = username,
            PasswordHash = _passwords.Hash(password),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Account {Username} created.", username);

        return user;
    }

    public async Task<bool> SetPasswordAsync(string username, string password, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null)
        {
            return false;
        }

        user.PasswordHash = _passwords.Hash(password);

        // A new password ends the old sessions. Someone who resets a password because it may have
        // leaked expects exactly that, and a reset that leaves the intruder signed in is worse
        // than useless: it looks like the problem was dealt with.
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await _dbContext.SaveChangesAsync(cancellationToken);

        _throttle.Reset(username);

        _logger.LogInformation("Password for {Username} was reset and existing sessions ended.", username);

        return true;
    }

    /// <summary>
    /// Deletes an account. The last one cannot go: the system would lock itself out, with
    /// nobody left who could sign in and undo it.
    /// </summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (await _dbContext.Users.CountAsync(cancellationToken) <= 1)
        {
            return false;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
        {
            return false;
        }

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Account {Username} was deleted.", user.Username);

        return true;
    }
}
