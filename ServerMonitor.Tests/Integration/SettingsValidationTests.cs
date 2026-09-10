using System.Net;
using System.Net.Http.Json;

namespace ServerMonitor.Tests.Integration;

/// <summary>
/// The two thresholds per metric, as the API accepts and refuses them.
/// </summary>
/// <remarks>
/// The rule itself is tested in isolation. What only a running API can show is that the new
/// columns exist, that a fresh installation arrives with sensible warnings, and that a setting
/// which could never fire is refused rather than stored.
/// </remarks>
[Collection(ApiCollection.Name)]
public class SettingsValidationTests
{
    private readonly ApiFixture _api;

    public SettingsValidationTests(ApiFixture api)
    {
        _api = api;
    }

    private sealed record Settings(
        double CpuThreshold,
        double MemoryThreshold,
        double DiskThreshold,
        double CpuWarningThreshold,
        double MemoryWarningThreshold,
        double DiskWarningThreshold,
        bool AlertsEnabled,
        int OfflineAfterSeconds,
        bool HeartbeatAlertsEnabled);

    private static async Task<Settings> ReadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Settings>("/api/settings"))!;

    [Fact]
    public async Task AFreshInstallation_HasWarningsBelowCritical()
    {
        using var client = _api.CreateServiceClient();

        var settings = await ReadAsync(client);

        // Seeded at 75 against the critical 90. The migration derives the same figure for an
        // existing installation from whatever critical threshold it already had.
        Assert.Equal(75, settings.CpuWarningThreshold);
        Assert.Equal(75, settings.MemoryWarningThreshold);
        Assert.Equal(75, settings.DiskWarningThreshold);
        Assert.True(settings.CpuWarningThreshold < settings.CpuThreshold);
    }

    [Fact]
    public async Task AWarningAboveCritical_IsRefusedAndChangesNothing()
    {
        using var client = _api.CreateServiceClient();

        var before = await ReadAsync(client);

        var response = await client.PutAsJsonAsync("/api/settings",
            before with { CpuWarningThreshold = before.CpuThreshold + 5 });

        // A warning above critical could never fire: every value past it is already critical.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadAsync(client));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task AWarningOutsideZeroToHundred_IsRefused(double warning)
    {
        using var client = _api.CreateServiceClient();

        var before = await ReadAsync(client);

        var response = await client.PutAsJsonAsync("/api/settings",
            before with { MemoryWarningThreshold = warning });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadAsync(client));
    }

    [Fact]
    public async Task AWarningEqualToCritical_IsAcceptedAsWarningsOff()
    {
        using var client = _api.CreateServiceClient();

        var before = await ReadAsync(client);

        try
        {
            var response = await client.PutAsJsonAsync("/api/settings",
                before with { DiskWarningThreshold = before.DiskThreshold });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(before.DiskThreshold, (await ReadAsync(client)).DiskWarningThreshold);
        }
        finally
        {
            // Settings are shared by the whole collection and are not reset between tests.
            await client.PutAsJsonAsync("/api/settings", before);
        }
    }
}
