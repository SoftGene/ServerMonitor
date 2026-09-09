using Microsoft.EntityFrameworkCore;
using ServerMonitor.Infrastructure.Auth;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Commands;

/// <summary>
/// Emergency password reset from the server: <c>dotnet run --project ServerMonitor.Api -- reset-password name</c>
/// </summary>
/// <remarks>
/// The only recovery path for a forgotten password. There is no delivery channel (email) in
/// this project, and tying sign-in to Telegram would restore the dependency on a notification
/// channel that the previous stage deliberately removed.
///
/// This is not a security hole. Whoever can run a command on the server already holds the
/// database connection string and could replace the hash by hand; the command only saves them
/// the trouble. The access boundary here is <b>access to the machine</b> rather than to the
/// application, which is the ordinary and deliberate arrangement for self-hosted software.
/// </remarks>
public static class ResetPasswordCommand
{
    private const int MinPasswordLength = 8;

    public static async Task<int> RunAsync(string username)
    {
        // Its own configuration rather than WebApplication.CreateBuilder: that one parses
        // args as configuration keys and would throw a FormatException on a bare word like
        // "reset-password".
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets(typeof(ResetPasswordCommand).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Connection string 'DefaultConnection' is not configured.");

            return 1;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new AppDbContext(options);

        // Bring the schema up to date first. The web host does this at startup, but this command
        // deliberately runs before the host is built — so on a database that has not been started
        // since the last upgrade, every query below fails with a raw "column does not exist" from
        // PostgreSQL.
        //
        // That is the worst possible moment for it: this is the only way back into a system whose
        // password has been lost, and it would break precisely for someone who had just upgraded.
        // Announced rather than silent, because a password reset quietly changing the schema would
        // be a surprise of a different kind.
        var pending = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();

        if (pending.Count > 0)
        {
            Console.WriteLine($"The database is {pending.Count} migration(s) behind; applying them first:");

            foreach (var migration in pending)
            {
                Console.WriteLine($"  {migration}");
            }

            await dbContext.Database.MigrateAsync();

            Console.WriteLine("Schema is up to date.");
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Username == username);

        if (user is null)
        {
            // Unlike the login form, there is nothing to conceal here: the person running
            // this owns the server, and a typo in the name is worth telling them about.
            Console.Error.WriteLine($"No account named '{username}'.");

            var existing = await dbContext.Users.Select(u => u.Username).ToListAsync();

            if (existing.Count > 0)
            {
                Console.Error.WriteLine("Known accounts: " + string.Join(", ", existing));
            }

            return 1;
        }

        Console.WriteLine($"Resetting the password for '{user.Username}'.");

        var password = ReadSecret("New password: ");

        if (password is null)
        {
            return 1;
        }

        if (password.Length < MinPasswordLength)
        {
            Console.Error.WriteLine($"Password must be at least {MinPasswordLength} characters.");

            return 1;
        }

        var confirmation = ReadSecret("Repeat password: ");

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Passwords do not match.");

            return 1;
        }

        user.PasswordHash = new PasswordService().Hash(password);
        await dbContext.SaveChangesAsync();

        Console.WriteLine($"Password for '{user.Username}' has been reset.");

        return 0;
    }

    /// <summary>
    /// Reads a password without echoing it to the screen.
    /// </summary>
    /// <remarks>
    /// The password is read from standard input rather than from a command argument:
    /// arguments land in shell history and are visible in the process list (<c>ps</c>) to
    /// every user on the machine. The typing is not echoed so it does not survive in the
    /// terminal scrollback.
    /// </remarks>
    private static string? ReadSecret(string prompt)
    {
        Console.Write(prompt);

        try
        {
            var characters = new List<char>();

            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();

                    return new string(characters.ToArray());
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (characters.Count > 0)
                    {
                        characters.RemoveAt(characters.Count - 1);
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine();
                    Console.Error.WriteLine("Cancelled.");

                    return null;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    characters.Add(key.KeyChar);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Input is redirected — a script rather than a terminal — so reading key by key
            // is not available. Fall back to a line and say plainly that it was not hidden.
            Console.WriteLine();
            Console.Error.WriteLine("Warning: input is redirected, so the password was not hidden.");

            return Console.ReadLine();
        }
    }
}
