namespace ServerMonitor.Tests.Monitoring;

/// <summary>
/// Тест, который запускает настоящий сбор метрик и потому загружает сборку домена
/// из папки тестов.
/// <para>
/// На машинах с включённым Smart App Control (Windows: «Управление приложениями») загрузка
/// свежесобранной неподписанной сборки блокируется с ошибкой 0x800711C7 — это политика
/// системы, а не дефект кода. Поэтому такие тесты по умолчанию пропускаются и включаются
/// переменной окружения:
/// </para>
/// <code>
/// SERVERMONITOR_RUN_COLLECTOR_TESTS=1 dotnet test
/// </code>
/// </summary>
public sealed class LocalCollectorFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "SERVERMONITOR_RUN_COLLECTOR_TESTS";

    public LocalCollectorFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
        {
            Skip = $"Установи {EnvironmentVariable}=1, чтобы запустить тесты реального сбора метрик.";
        }
    }
}
