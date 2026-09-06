using System.Runtime.InteropServices;
using System.Text.Json;

namespace ServerMonitor.Agent;

/// <summary>
/// Выданные при регистрации идентификатор и ключ. Хранятся рядом с бинарником, потому что
/// пережить перезапуск обязаны: повторная регистрация завела бы на сервере вторую запись
/// для той же машины.
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

        // Ключ — секрет уровня пароля: на Unix закрываем файл от всех, кроме владельца.
        // На Windows права наследуются от каталога, отдельного шага не требуется.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
