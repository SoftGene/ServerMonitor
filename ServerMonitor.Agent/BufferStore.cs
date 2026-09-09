using System.Text.Json;

namespace ServerMonitor.Agent;

/// <summary>
/// Keeps the unsent readings on disk, so an outage plus a restart is not the same as data loss.
/// </summary>
/// <remarks>
/// The buffer exists because the server can be unreachable. Holding it only in memory meant the
/// two failures people actually hit together — the server is down, and the machine reboots or the
/// service restarts — combined into exactly the loss the buffer was there to prevent.
/// <para>
/// Nothing secret is written here: these are readings, not keys. That is the difference between
/// this file and <c>agent-state.json</c>, which is closed to its owner alone.
/// </para>
/// </remarks>
public class BufferStore
{
    private readonly string _path;
    private readonly ILogger<BufferStore> _logger;

    public BufferStore(string path, ILogger<BufferStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    /// <summary>
    /// Reads what the previous run left behind. Never throws.
    /// </summary>
    /// <remarks>
    /// A file that will not parse is deleted and the agent starts empty. Losing an hour of
    /// buffered readings is a small harm; an agent that will not start because of them is a
    /// machine nobody is watching at all, which is a much larger one.
    /// </remarks>
    public async Task<List<MetricReport>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_path);

            var items = await JsonSerializer.DeserializeAsync<List<MetricReport>>(
                stream, cancellationToken: cancellationToken);

            return items ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the buffered readings; starting with an empty buffer.");

            Delete();

            return [];
        }
    }

    /// <summary>
    /// Writes the buffer, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// Written to a temporary file and then moved into place. A move within one directory is
    /// atomic, so a crash halfway through leaves either the old file or the new one — never the
    /// half-written file that serialising straight over the target would produce, which is
    /// precisely the file the next start would fail to read.
    /// </remarks>
    public async Task SaveAsync(IReadOnlyList<MetricReport> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            Delete();

            return;
        }

        var temporaryPath = _path + ".tmp";

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, items, cancellationToken: cancellationToken);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failing to persist must not stop the agent from measuring. The readings are still
            // in memory and still being sent; only the ability to survive a restart is lost.
            _logger.LogWarning(ex, "Could not persist the buffered readings.");

            TryDelete(temporaryPath);
        }
    }

    /// <summary>Removes the file, which is what an empty buffer means on disk.</summary>
    public void Delete() => TryDelete(_path);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do about it, and nothing that depends on it succeeding.
        }
    }
}
