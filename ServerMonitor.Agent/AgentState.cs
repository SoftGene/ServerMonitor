using System.Runtime.InteropServices;
using System.Text.Json;

namespace ServerMonitor.Agent;

/// <summary>
/// The identifier and key handed out at registration. Kept next to the binary because they
/// have to survive a restart: registering again would create a second server record for the
/// same machine.
/// </summary>
public class AgentState
{
    public Guid ServerId { get; set; }
    public string ApiKey { get; set; } = string.Empty;

    public static async Task<AgentState?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);

        return await JsonSerializer.DeserializeAsync<AgentState>(stream, cancellationToken: cancellationToken);
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        await using (var stream = File.Create(path))
        {
            await JsonSerializer.SerializeAsync(stream, this, cancellationToken: cancellationToken);
        }

        // The key is a password-grade secret: on Unix the file is closed to everyone but its
        // owner. On Windows permissions are inherited from the directory, so no extra step.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
