using Microsoft.EntityFrameworkCore;
using ServerMonitor.Infrastructure.Auth;
using ServerMonitor.Infrastructure.Data;

namespace ServerMonitor.Api.Commands;

/// <summary>
/// Аварийный сброс пароля с сервера: <c>dotnet run --project ServerMonitor.Api -- reset-password имя</c>
/// </summary>
/// <remarks>
/// Единственный путь восстановления, когда пароль забыт: канала доставки (почты) в проекте нет,
/// а привязывать вход к Telegram означало бы вернуть зависимость от канала уведомлений, которую
/// предыдущий этап специально убрал.
///
/// Это не дыра в безопасности. У того, кто может выполнить команду на сервере, уже есть строка
/// подключения к базе — то есть он и так может подменить хеш вручную. Команда лишь избавляет от
/// возни. Граница доступа здесь — <b>доступ к машине</b>, а не к приложению; для self-hosted это
/// обычный и сознательный выбор.
/// </remarks>
public static class ResetPasswordCommand
{
    private const int MinPasswordLength = 8;

    public static async Task<int> RunAsync(string username)
    {
        // Своя конфигурация, а не через WebApplication.CreateBuilder: тот разбирает args как
        // ключи настроек и на голом слове «reset-password» упал бы с FormatException.
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

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Username == username);

        if (user is null)
        {
            // Здесь, в отличие от формы входа, скрывать нечего: команду выполняет владелец
            // сервера, и ему полезно узнать, что имя набрано с опечаткой.
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
    /// Читает пароль без отображения на экране.
    /// </summary>
    /// <remarks>
    /// Пароль читается из стандартного ввода, а не из аргумента команды: аргументы попадают в
    /// историю оболочки и видны в списке процессов (<c>ps</c>) всем пользователям машины.
    /// Ввод не отображается, чтобы он не остался в прокрутке терминала.
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
            // Ввод перенаправлен (запуск из скрипта) — посимвольное чтение недоступно.
            // Читаем строкой и честно предупреждаем, что скрыть ввод не получилось.
            Console.WriteLine();
            Console.Error.WriteLine("Warning: input is redirected, so the password was not hidden.");

            return Console.ReadLine();
        }
    }
}
