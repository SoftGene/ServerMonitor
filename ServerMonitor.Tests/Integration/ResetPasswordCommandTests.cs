using Npgsql;
using ServerMonitor.Api.Commands;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// The console reset, run against a database that has not caught up yet.
/// </summary>
/// <remarks>
/// This is the only way back into a system whose password has been lost, and it deliberately runs
/// before the web host is built — which is also what applies migrations. So on a database that has
/// not been started since the last upgrade, every query it makes used to fail with a raw
/// "column does not exist" from PostgreSQL: the recovery path broke for exactly the person who had
/// just upgraded.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ResetPasswordCommandTests
{
    private const string ConnectionStringKey = "ConnectionStrings__DefaultConnection";

    private readonly ApiFixture _api;

    public ResetPasswordCommandTests(ApiFixture api)
    {
        _api = api;
    }

    /// <summary>Creates an empty database in the same container and returns its connection string.</summary>
    /// <remarks>
    /// Empty rather than the suite's own database, because "behind on migrations" is the state
    /// under test and the shared one is always up to date.
    /// </remarks>
    private async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = "resetcmd_" + Guid.NewGuid().ToString("N")[..12];

        // CREATE DATABASE cannot run inside a transaction and cannot target the database being
        // created, so it is issued from the container's default one.
        var builder = new NpgsqlConnectionStringBuilder(_api.ConnectionString) { Database = "postgres" };

        await using (var connection = new NpgsqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = $"""CREATE DATABASE "{name}";""";

            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_api.ConnectionString) { Database = name }.ConnectionString;
    }

    [Fact]
    public async Task OnADatabaseThatIsBehind_ItMigratesInsteadOfFailing()
    {
        var connectionString = await CreateEmptyDatabaseAsync();
        var previous = Environment.GetEnvironmentVariable(ConnectionStringKey);

        // The command builds its own configuration, and environment variables are its last and
        // therefore winning source. Worth knowing: without this override it would read the
        // developer's own user secrets and talk to their real database.
        Environment.SetEnvironmentVariable(ConnectionStringKey, connectionString);

        try
        {
            var exitCode = await ResetPasswordCommand.RunAsync("nobody");

            // One, because no such account exists in a database that was empty a moment ago —
            // and reaching that answer at all is the point. Before the fix this threw a
            // PostgresException about a missing column and never got as far as looking.
            Assert.Equal(1, exitCode);

            Assert.True(await TableExistsAsync(connectionString, "Users"));
            Assert.True(await TableExistsAsync(connectionString, "MetricRollups"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringKey, previous);
        }
    }

    [Fact]
    public async Task WithNoConnectionStringAtAll_ItSaysSoRatherThanThrowing()
    {
        var previous = Environment.GetEnvironmentVariable(ConnectionStringKey);

        // Empty rather than null: an empty environment variable still shadows every other source,
        // which is what makes this the reliable way to test the missing-configuration path.
        Environment.SetEnvironmentVariable(ConnectionStringKey, string.Empty);

        try
        {
            Assert.Equal(1, await ResetPasswordCommand.RunAsync("anyone"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringKey, previous);
        }
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass($1) IS NOT NULL;";
        command.Parameters.AddWithValue($"public.\"{table}\"");

        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
