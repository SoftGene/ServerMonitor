using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerMonitor.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// Runs the real API against a real PostgreSQL in a throwaway container.
/// </summary>
/// <remarks>
/// These are not unit tests and are not trying to be. The things they cover — foreign keys,
/// unique indexes, the service-key filter, the order of middleware — are precisely the things
/// that cannot fail in a unit test because none of them exist there. Both of the silent defects
/// this project shipped were of that kind: the code was fine in isolation and the database
/// rejected it.
///
/// One container is shared by every test in the collection; starting Postgres per class would
/// dominate the run time. Tests clean up after themselves instead.
///
/// Docker has to be running. That is already true for developing this project at all.
/// </remarks>
public class ApiFixture : IAsyncLifetime
{
    public const string ServiceKey = "integration-test-service-key";
    public const string EnrollmentToken = "integration-test-enrollment-token";

    // The image goes to the constructor rather than WithImage: the parameterless overload is
    // obsolete. Pinned to the same major version the project runs in production.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("servermonitor_tests")
        .Build();

    private WebApplicationFactory<Program> _factory = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
                builder.UseSetting("Api:ServiceKey", ServiceKey);
                builder.UseSetting("Agents:EnrollmentToken", EnrollmentToken);

                // Left empty on purpose: the alerting must work without a delivery channel, and
                // a test run should never try to reach Telegram.
                builder.UseSetting("Telegram:BotToken", string.Empty);
                builder.UseSetting("Telegram:ChatId", string.Empty);

                // A short window and a small batch, so the retention tests can create "old"
                // readings without dating them a month back and can force more than one batch
                // with a manageable number of rows. The background sweep waits a minute before
                // its first pass, which is far longer than the whole suite takes, so it never
                // races a test.
                builder.UseSetting("Retention:SnapshotDays", "7");
                builder.UseSetting("Retention:BatchSize", "100");

                // Raised out of the way. The suite registers agents and signs in dozens of times
                // against one host, which is nothing like how either endpoint is used in life;
                // the limiter itself gets its own host with its own low limits.
                builder.UseSetting("RateLimits:EnrollmentPerHour", "10000");
                builder.UseSetting("RateLimits:CredentialsPerMinute", "10000");
            });

        // Forces the host to build, which is also what applies the migrations.
        using var _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// The container's connection string, for a test that needs a host of its own.
    /// </summary>
    /// <remarks>
    /// Most tests share the one host, which is what keeps the suite fast. A setting that has to
    /// differ — a rate limit low enough to reach without waiting an hour — cannot be changed on a
    /// running host, so such a test builds its own against the same database.
    /// </remarks>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>A client with no service key — used to prove that endpoints refuse it.</summary>
    public HttpClient CreateAnonymousClient() => _factory.CreateClient();

    /// <summary>A client that carries the service key, standing in for the web app.</summary>
    public HttpClient CreateServiceClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", ServiceKey);

        return client;
    }

    /// <summary>A client carrying an agent's own key, standing in for an agent.</summary>
    public HttpClient CreateAgentClient(string apiKey)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        return client;
    }

    public async Task<T> WithDbContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await action(dbContext);
    }

    /// <summary>Resolves a scoped service from the running host and hands it to the test.</summary>
    public async Task<TResult> WithServiceAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();

        return await action(service);
    }

    /// <summary>
    /// Empties the tables a test may have written to. Settings are left alone: they are seeded
    /// by the model and other tests rely on the thresholds being there.
    /// </summary>
    public async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Servers cascade to snapshots and alerts, so one truncate clears three tables.
        await dbContext.Database.ExecuteSqlRawAsync(
            """TRUNCATE "Servers", "Users" RESTART IDENTITY CASCADE;""");
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
