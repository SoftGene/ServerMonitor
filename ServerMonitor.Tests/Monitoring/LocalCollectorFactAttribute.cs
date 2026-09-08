namespace ServerMonitor.Tests.Monitoring;

/// <summary>
/// A test that runs real metric collection and therefore loads the domain assembly from the
/// test output folder.
/// <para>
/// On machines with Smart App Control enabled, loading a freshly built unsigned assembly is
/// blocked with error 0x800711C7 — a system policy rather than a defect in the code. Such
/// tests are therefore skipped by default and switched on with an environment variable:
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
            Skip = $"Set {EnvironmentVariable}=1 to run the real metric collection tests.";
        }
    }
}
